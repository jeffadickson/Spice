use crate::{
    api::{Analysis, Request},
    circuit::{Circuit, Element},
    error::{ErrorCode, SimError, SimResult},
    ids::NodeId,
    quantity::spice_scalar,
    sources::SourceOverride,
    waveform::Waveform,
    waveform::Point,
};
/// Explicit small subset: optional title, R/C/L/D/V/I, .op and .end. No includes or control commands.
pub fn parse(text: &str) -> SimResult<Circuit> {
    if text.len() > 1_048_576 {
        return Err(SimError::new(
            ErrorCode::ResourceExhausted,
            "netlist exceeds 1 MiB",
        ));
    }
    let mut circuit = Circuit {
        nodes: vec!["0".into()],
        elements: Vec::new(),
    };
    let mut ended = false;
    for (index, line) in text.lines().enumerate() {
        let line = line.trim();
        if line.is_empty() || line.starts_with('*') {
            continue;
        }
        let tokens: Vec<&str> = line.split_whitespace().collect();
        let name = tokens[0];
        if ended {
            return Err(SimError::invalid(format!(
                "line {}: content after .end",
                index + 1
            )));
        }
        if name.eq_ignore_ascii_case(".end") {
            if tokens.len() != 1 {
                return Err(SimError::invalid("invalid .end"));
            }
            ended = true;
            continue;
        }
        if name.eq_ignore_ascii_case(".op") && tokens.len() == 1 {
            continue;
        }
        let kind = name.chars().next().unwrap().to_ascii_uppercase();
        // Only an explicit title: line or a single-token first line is a title.
        // A malformed first device line must never be silently discarded.
        if index == 0
            && !name.starts_with('.')
            && (tokens.len() == 1 || name.eq_ignore_ascii_case("title:"))
        {
            continue;
        }
        if !matches!(kind, 'R' | 'C' | 'L' | 'D' | 'V' | 'I' | 'E' | 'G') {
            return Err(SimError::new(
                ErrorCode::Unsupported,
                format!("line {}: unsupported element/directive", index + 1),
            ));
        }
        let expected = match kind { 'D' => 5, 'E' | 'G' => 6, _ => 4 };
        if tokens.len() != expected {
            return Err(SimError::invalid(format!(
                "line {}: expected id p n value{}",
                index + 1,
                if kind == 'D' { " thermal_voltage" } else { "" }
            )));
        }
        let mut node = |label: &str| -> NodeId {
            let label = label.to_ascii_lowercase();
            if let Some(i) = circuit.nodes.iter().position(|v| v == &label) {
                NodeId(i)
            } else {
                circuit.nodes.push(label);
                NodeId(circuit.nodes.len() - 1)
            }
        };
        let p = node(tokens[1]);
        let n = node(tokens[2]);
        let cp = if matches!(kind, 'E' | 'G') { Some(node(tokens[3])) } else { None };
        let cn = if matches!(kind, 'E' | 'G') { Some(node(tokens[4])) } else { None };
        let value = spice_scalar(if matches!(kind, 'E' | 'G') { tokens[5] } else { tokens[3] })
            .map_err(|e| SimError::invalid(format!("line {}: {}", index + 1, e.message)))?;
        let id = name.to_ascii_uppercase();
        circuit.elements.push(match kind {
            'R' => Element::Resistor {
                id,
                p,
                n,
                ohms: value,
            },
            'C' => Element::Capacitor {
                id,
                p,
                n,
                farads: value,
            },
            'V' => Element::Voltage {
                id,
                p,
                n,
                volts: value,
            },
            'I' => Element::Current {
                id,
                p,
                n,
                amps: value,
            },
            'L' => Element::Inductor {
                id,
                p,
                n,
                henries: value,
            },
            'D' => Element::Diode {
                id,
                p,
                n,
                saturation_amps: value,
                thermal_volts: spice_scalar(tokens[4])
                    .map_err(|e| SimError::invalid(format!("line {}: {}", index + 1, e.message)))?,
            },
            'G' => Element::Vccs {
                id,
                p,
                n,
                cp: cp.expect("G control positive node"),
                cn: cn.expect("G control negative node"),
                siemens: value,
            },
            'E' => Element::Vcvs {
                id,
                p,
                n,
                cp: cp.expect("E control positive node"),
                cn: cn.expect("E control negative node"),
                gain: value,
            },
            _ => unreachable!(),
        });
        if circuit.elements.len() > 1024 || circuit.nodes.len() > 128 {
            return Err(SimError::new(
                ErrorCode::ResourceExhausted,
                "netlist circuit limit exceeded",
            ));
        }
    }
    circuit.validate()?;
    Ok(circuit)
}

/// Import a deliberately narrow, documented ngspice compatibility subset.
/// The importer accepts the mandatory title line, DC source marker and analysis/
/// output directives needed by the curated BSD-licensed regression fixtures. It
/// does not claim general ngspice parsing or execute control blocks.
pub fn parse_ngspice_basic(text: &str) -> SimResult<Circuit> {
    let mut normalized = String::new();
    for (index, source_line) in text.lines().enumerate() {
        if index == 0 {
            continue;
        }
        // ngspice accepts a semicolon as an inline comment introducer. Strip it
        // before directive/device recognition so comment words cannot become
        // accidental device parameters in this deliberately bounded importer.
        let line = source_line.split(';').next().unwrap_or_default().trim();
        if line.is_empty() || line.starts_with('*') {
            continue;
        }
        let head = line.split_whitespace().next().unwrap_or_default();
        if [".options", ".tran", ".ac", ".plot", ".print", ".probe"]
            .iter()
            .any(|directive| head.eq_ignore_ascii_case(directive))
        {
            continue;
        }
        let mut tokens: Vec<&str> = line.split_whitespace().collect();
        if matches!(head.chars().next().map(|c| c.to_ascii_uppercase()), Some('V' | 'I'))
            && tokens.get(3).is_some_and(|token| token.eq_ignore_ascii_case("pulse"))
            && tokens.len() == 11
        {
            // The typed transient override owns waveform semantics; retain the
            // pulse low level as the DC initialization value for compilation.
            tokens[3] = tokens[4];
            tokens.truncate(4);
        }
        if matches!(head.chars().next().map(|c| c.to_ascii_uppercase()), Some('V' | 'I'))
            && tokens.get(3).is_some_and(|token| token.eq_ignore_ascii_case("dc"))
        {
            tokens.remove(3);
        }
        // ngspice also permits the compact DC=<value> spelling. Normalize only
        // this unambiguous form; a source with additional waveform or AC terms
        // must remain rejected rather than silently losing its behavior.
        if matches!(head.chars().next().map(|c| c.to_ascii_uppercase()), Some('V' | 'I'))
            && tokens.len() == 4
            && tokens[3].get(..3).is_some_and(|prefix| prefix.eq_ignore_ascii_case("dc="))
        {
            tokens[3] = &tokens[3][3..];
        }
        normalized.push_str(&tokens.join(" "));
        normalized.push('\n');
    }
    parse(&normalized)
}

/// Lowers the bounded ngspice deck profile into the same typed request contract
/// used by JSON and SForge callers. Only a two-value .tran directive is
/// accepted; optional arguments fail rather than being ignored.
pub fn import_ngspice_request(text: &str, case_id: impl Into<String>) -> SimResult<Request> {
    let mut transient = None;
    let mut ac = None;
    for source_line in text.lines() {
        let line = source_line.split(';').next().unwrap_or_default().trim();
        let tokens: Vec<_> = line.split_whitespace().collect();
        if tokens.first().is_some_and(|token| token.eq_ignore_ascii_case(".tran")) {
            if transient.is_some() || tokens.len() != 3 {
                return Err(SimError::new(
                    ErrorCode::Unsupported,
                    "ngspice importer supports exactly one .tran step stop directive",
                ));
            }
            transient = Some(crate::configured::TransientConfig {
                max_step_s: spice_scalar(tokens[1])?,
                stop_s: spice_scalar(tokens[2])?,
                ..Default::default()
            });
        }
        if tokens.first().is_some_and(|token| token.eq_ignore_ascii_case(".ac")) {
            if ac.is_some()
                || tokens.len() != 5
                || !tokens[1].eq_ignore_ascii_case("lin")
            {
                return Err(SimError::new(
                    ErrorCode::Unsupported,
                    "ngspice importer supports exactly one .ac lin count start stop directive",
                ));
            }
            let count: usize = tokens[2]
                .parse()
                .map_err(|_| SimError::invalid("invalid AC linear point count"))?;
            if !(1..=10_000).contains(&count) {
                return Err(SimError::invalid("AC linear point count must be 1..10000"));
            }
            let start = spice_scalar(tokens[3])?;
            let stop = spice_scalar(tokens[4])?;
            if !start.is_finite() || !stop.is_finite() || start <= 0. || stop < start {
                return Err(SimError::invalid("invalid AC linear frequency range"));
            }
            let frequencies_hz = (0..count)
                .map(|i| start + (stop - start) * i as f64 / (count - 1).max(1) as f64)
                .collect();
            ac = Some(frequencies_hz);
        }
    }
    let analysis = match (transient, ac) {
        (Some(_), Some(_)) => return Err(SimError::new(ErrorCode::Unsupported, "only one ngspice analysis directive is supported")),
        (Some(options), None) => Analysis::TransientConfigured { options },
        (None, Some(frequencies_hz)) => Analysis::Ac { frequencies_hz },
        (None, None) => Analysis::OperatingPoint,
    };
    let input = if matches!(analysis, Analysis::Ac { .. }) {
        let mut normalized = String::new();
        for line in text.lines() {
            let tokens: Vec<_> = line.split_whitespace().collect();
            if matches!(tokens.first().and_then(|name| name.chars().next()).map(|kind| kind.to_ascii_uppercase()), Some('V' | 'I'))
                && tokens.len() == 7
                && tokens[3].eq_ignore_ascii_case("dc")
                && tokens[5].eq_ignore_ascii_case("ac")
            {
                normalized.push_str(&format!("{} {} {} {}\n", tokens[0], tokens[1], tokens[2], tokens[6]));
            } else if tokens.len() == 5
                && tokens.first().is_some_and(|name| name.starts_with('R') || name.starts_with('r'))
                && tokens[4].get(..3).is_some_and(|prefix| prefix.eq_ignore_ascii_case("ac="))
            {
                normalized.push_str(&format!("{} {} {} {}\n", tokens[0], tokens[1], tokens[2], &tokens[4][3..]));
            } else {
                normalized.push_str(line);
                normalized.push('\n');
            }
        }
        normalized
    } else {
        text.lines().map(|line| {
            let tokens: Vec<_> = line.split_whitespace().collect();
            if tokens.len() == 4 && (tokens[3].get(..4).is_some_and(|prefix| prefix.eq_ignore_ascii_case("sin("))
                || tokens[3].get(..4).is_some_and(|prefix| prefix.eq_ignore_ascii_case("pwl(")))
                && tokens[3].ends_with(')') {
                let offset = tokens[3][4..tokens[3].len() - 1].split(',').next().unwrap_or_default();
                format!("{} {} {} {}", tokens[0], tokens[1], tokens[2], offset)
            } else { line.into() }
        }).collect::<Vec<String>>().join("\n")
    };
    let mut sources = Vec::new();
    for source_line in text.lines() {
        let line = source_line.split(';').next().unwrap_or_default().trim();
        let tokens: Vec<_> = line.split_whitespace().collect();
        let Some(name) = tokens.first() else { continue };
        if !matches!(name.chars().next().map(|c| c.to_ascii_uppercase()), Some('V' | 'I')) {
            continue;
        }
        if tokens.len() == 11 && tokens[3].eq_ignore_ascii_case("pulse") {
            let values: SimResult<Vec<f64>> = tokens[4..].iter().map(|value| spice_scalar(value)).collect();
            let values = values?;
            sources.push(SourceOverride {
                id: name.to_ascii_uppercase(),
                waveform: Waveform::Pulse {
                    low: values[0], high: values[1], delay_s: values[2], rise_s: values[3],
                    fall_s: values[4], width_s: values[5], period_s: values[6],
                },
            });
        }
        if tokens.len() == 4 && tokens[3].get(..4).is_some_and(|prefix| prefix.eq_ignore_ascii_case("sin("))
            && tokens[3].ends_with(')') {
            let values: SimResult<Vec<f64>> = tokens[3][4..tokens[3].len() - 1].split(',').map(spice_scalar).collect();
            let values = values?;
            if values.len() != 3 {
                return Err(SimError::new(ErrorCode::Unsupported, "bounded SIN source requires offset,amplitude,frequency"));
            }
            sources.push(SourceOverride { id: name.to_ascii_uppercase(), waveform: Waveform::Sine {
                offset: values[0], amplitude: values[1], frequency_hz: values[2], phase_rad: 0.,
            }});
        }
        if tokens.len() == 4 && tokens[3].get(..4).is_some_and(|prefix| prefix.eq_ignore_ascii_case("pwl("))
            && tokens[3].ends_with(')') {
            let values: SimResult<Vec<f64>> = tokens[3][4..tokens[3].len() - 1].split(',').map(spice_scalar).collect();
            let values = values?;
            if values.len() < 2 || values.len() % 2 != 0 {
                return Err(SimError::new(ErrorCode::Unsupported, "bounded PWL source requires time,value pairs"));
            }
            sources.push(SourceOverride { id: name.to_ascii_uppercase(), waveform: Waveform::Pwl {
                points: values.chunks_exact(2).map(|pair| Point { time_s: pair[0], value: pair[1] }).collect(),
            }});
        }
    }
    let analysis = match analysis {
        Analysis::TransientConfigured { mut options } => {
            options.sources = sources;
            Analysis::TransientConfigured { options }
        }
        _ if !sources.is_empty() => return Err(SimError::new(ErrorCode::Unsupported, "PULSE sources require a bounded .tran directive")),
        other => other,
    };
    Ok(Request {
        schema_version: "1".into(),
        case_id: case_id.into(),
        circuit: parse_ngspice_basic(&input)?,
        analysis,
        backend: "lu".into(),
        experiment: None,
    })
}
