use crate::{
    api::{AcSourcePhasor, Analysis, Request},
    circuit::{Circuit, Element},
    error::{ErrorCode, SimError, SimResult},
    ids::NodeId,
    quantity::spice_scalar,
    sources::SourceOverride,
    waveform::Point,
    waveform::Waveform,
};
/// Explicit small subset: optional title, R/C/L/D/V/I/E/F/G/H, .op and .end.
/// No includes or control commands are executed.
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
        if !matches!(
            kind,
            'R' | 'C' | 'L' | 'D' | 'V' | 'I' | 'E' | 'F' | 'G' | 'H'
        ) {
            return Err(SimError::new(
                ErrorCode::Unsupported,
                format!("line {}: unsupported element/directive", index + 1),
            ));
        }
        let expected = match kind {
            'D' | 'F' | 'H' => 5,
            'E' | 'G' => 6,
            _ => 4,
        };
        if tokens.len() != expected && !(kind == 'D' && tokens.len() == 6) {
            return Err(SimError::invalid(format!(
                "line {}: expected id p n value{}",
                index + 1,
                if kind == 'D' { " thermal_voltage" } else { "" }
            )));
        }
        let mut node = |label: &str| -> NodeId {
            let label = if label.eq_ignore_ascii_case("gnd") || label.eq_ignore_ascii_case("ground")
            {
                "0".into()
            } else {
                label.to_ascii_lowercase()
            };
            if let Some(i) = circuit.nodes.iter().position(|v| v == &label) {
                NodeId(i)
            } else {
                circuit.nodes.push(label);
                NodeId(circuit.nodes.len() - 1)
            }
        };
        let p = node(tokens[1]);
        let n = node(tokens[2]);
        let cp = if matches!(kind, 'E' | 'G') {
            Some(node(tokens[3]))
        } else {
            None
        };
        let cn = if matches!(kind, 'E' | 'G') {
            Some(node(tokens[4]))
        } else {
            None
        };
        let value = spice_scalar(if matches!(kind, 'E' | 'G') {
            tokens[5]
        } else if matches!(kind, 'F' | 'H') {
            tokens[4]
        } else {
            tokens[3]
        })
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
            'D' if tokens.len() == 6 => Element::DiodeWithCapacitance {
                id,
                p,
                n,
                saturation_amps: value,
                thermal_volts: spice_scalar(tokens[4])?,
                farads: spice_scalar(tokens[5])?,
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
            // SPICE F sources are controlled by a named voltage-source branch,
            // rather than a pair of control terminals as used by E/G sources.
            'F' => Element::Cccs {
                id,
                p,
                n,
                control_id: tokens[3].to_ascii_uppercase(),
                gain: value,
            },
            // SPICE H sources use the same named control branch as F sources.
            'H' => Element::Ccvs {
                id,
                p,
                n,
                control_id: tokens[3].to_ascii_uppercase(),
                ohms: value,
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
    if text.len() > 1_048_576 {
        return Err(SimError::new(
            ErrorCode::ResourceExhausted,
            "ngspice deck exceeds 1 MiB",
        ));
    }
    // Fold only the documented continuation marker before token handling. This
    // keeps physical input-line limits independent from the bounded grammar.
    let mut logical_lines: Vec<String> = Vec::new();
    for source_line in text.lines().skip(1) {
        let line = source_line.split(';').next().unwrap_or_default().trim();
        if line.is_empty() || line.starts_with('*') {
            continue;
        }
        if let Some(tail) = line.strip_prefix('+') {
            let prior = logical_lines
                .last_mut()
                .ok_or_else(|| SimError::invalid("ngspice continuation has no preceding record"))?;
            if tail.trim().is_empty() {
                return Err(SimError::invalid("ngspice continuation is empty"));
            }
            prior.push(' ');
            prior.push_str(tail.trim());
        } else {
            logical_lines.push(line.to_owned());
        }
    }
    let logical_lines = crate::ngspice_resistor::lower(logical_lines)?;
    let thermal_voltage = bounded_temperature(&logical_lines)?;
    let diode_models = bounded_diode_models(&logical_lines, thermal_voltage)?;
    let mut normalized = String::new();
    for line in logical_lines {
        // ngspice accepts a semicolon as an inline comment introducer. Strip it
        // before directive/device recognition so comment words cannot become
        // accidental device parameters in this deliberately bounded importer.
        let head = line.split_whitespace().next().unwrap_or_default();
        let directive_tokens: Vec<_> = line.split_whitespace().collect();
        if head.eq_ignore_ascii_case(".print") || head.eq_ignore_ascii_case(".plot") {
            // Preserve this output syntax as import-only metadata for now, but
            // validate its bounded shape so malformed requests are not erased.
            if directive_tokens.len() < 3
                || !["op", "dc", "tran", "ac"]
                    .iter()
                    .any(|analysis| directive_tokens[1].eq_ignore_ascii_case(analysis))
            {
                return Err(SimError::invalid("invalid bounded .print/.plot directive"));
            }
            continue;
        }
        if head.eq_ignore_ascii_case(".probe") {
            if directive_tokens.len() < 2 {
                return Err(SimError::invalid("invalid bounded .probe directive"));
            }
            continue;
        }
        if head.eq_ignore_ascii_case(".options") {
            // These named options affect listing/output formatting only. Their
            // accepted combination is intentionally closed: numerical options
            // must never be silently discarded by bounded lowering.
            if directive_tokens.len() < 2
                || directive_tokens[1..].iter().any(|option| {
                    !["list", "node", "post", "trans", "noacct"]
                        .iter()
                        .any(|allowed| option.eq_ignore_ascii_case(allowed))
                })
            {
                return Err(SimError::new(
                    ErrorCode::Unsupported,
                    "unsupported bounded .options directive",
                ));
            }
            continue;
        }
        if [".tran", ".ac", ".model", ".temp"]
            .iter()
            .any(|directive| head.eq_ignore_ascii_case(directive))
        {
            continue;
        }
        let mut tokens: Vec<&str> = line.split_whitespace().collect();
        if head
            .chars()
            .next()
            .is_some_and(|kind| kind.eq_ignore_ascii_case(&'D'))
            && tokens.len() == 4
        {
            let model = diode_models
                .get(&tokens[3].to_ascii_uppercase())
                .ok_or_else(|| SimError::invalid("unknown bounded diode model"))?;
            normalized.push_str(&format!(
                "{} {} {} {} {}",
                tokens[0], tokens[1], tokens[2], model.saturation_amps, model.thermal_volts
            ));
            if let Some(farads) = model.constant_farads {
                normalized.push_str(&format!(" {farads}"));
            }
            normalized.push('\n');
            continue;
        }
        if matches!(
            head.chars().next().map(|c| c.to_ascii_uppercase()),
            Some('V' | 'I')
        ) && tokens
            .get(3)
            .is_some_and(|token| token.eq_ignore_ascii_case("pulse"))
            && tokens.len() == 11
        {
            // The typed transient override owns waveform semantics; retain the
            // pulse low level as the DC initialization value for compilation.
            tokens[3] = tokens[4];
            tokens.truncate(4);
        }
        if matches!(
            head.chars().next().map(|c| c.to_ascii_uppercase()),
            Some('V' | 'I')
        ) && tokens
            .get(3)
            .is_some_and(|token| token.eq_ignore_ascii_case("sin"))
            && matches!(tokens.len(), 7 | 9 | 10)
        {
            // The independently callable basic importer must retain the SIN
            // phase-biased time-zero value too, not just its offset.
            let values = tokens[4..]
                .iter()
                .map(|v| spice_scalar(v))
                .collect::<SimResult<Vec<_>>>()?;
            let initial = bounded_sine(values)?.at(0.)?;
            normalized.push_str(&format!(
                "{} {} {} {}\n",
                tokens[0], tokens[1], tokens[2], initial
            ));
            continue;
        }
        if matches!(
            head.chars().next().map(|c| c.to_ascii_uppercase()),
            Some('V' | 'I')
        ) && tokens
            .get(3)
            .is_some_and(|token| token.eq_ignore_ascii_case("exp"))
            && tokens.len() == 10
        {
            tokens[3] = tokens[4];
            tokens.truncate(4);
        }
        if matches!(
            head.chars().next().map(|c| c.to_ascii_uppercase()),
            Some('V' | 'I')
        ) && tokens
            .get(3)
            .is_some_and(|token| token.eq_ignore_ascii_case("pwl"))
            && tokens.len() >= 6
            && (tokens.len() - 4).is_multiple_of(2)
        {
            tokens[3] = tokens[5];
            tokens.truncate(4);
        }
        if matches!(
            head.chars().next().map(|c| c.to_ascii_uppercase()),
            Some('V' | 'I')
        ) && tokens
            .get(3)
            .is_some_and(|token| token.eq_ignore_ascii_case("dc"))
        {
            tokens.remove(3);
        }
        // ngspice also permits the compact DC=<value> spelling. Normalize only
        // this unambiguous form; a source with additional waveform or AC terms
        // must remain rejected rather than silently losing its behavior.
        if matches!(
            head.chars().next().map(|c| c.to_ascii_uppercase()),
            Some('V' | 'I')
        ) && tokens.len() == 4
            && tokens[3]
                .get(..3)
                .is_some_and(|prefix| prefix.eq_ignore_ascii_case("dc="))
        {
            tokens[3] = &tokens[3][3..];
        }
        if matches!(
            head.chars().next().map(|c| c.to_ascii_uppercase()),
            Some('V' | 'I')
        ) && tokens.len() == 4
        {
            tokens[3] = tokens[3].trim_end_matches(['V', 'v', 'A', 'a']);
        }
        normalized.push_str(&tokens.join(" "));
        normalized.push('\n');
    }
    parse(&normalized)
}

/// Named SI parameters for the bounded current/constant-charge profiles.
struct BoundedDiodeModel {
    saturation_amps: f64,
    thermal_volts: f64,
    constant_farads: Option<f64>,
}

/// Parse only declared equation semantics. Nonzero grading, transport, series
/// and unknown parameters are rejected rather than mapped onto a simpler model.
fn bounded_diode_models(
    lines: &[String],
    thermal_voltage: f64,
) -> SimResult<std::collections::BTreeMap<String, BoundedDiodeModel>> {
    let mut models = std::collections::BTreeMap::new();
    for line in lines {
        let tokens: Vec<_> = line.split_whitespace().collect();
        if !tokens
            .first()
            .is_some_and(|token| token.eq_ignore_ascii_case(".model"))
        {
            continue;
        }
        if tokens.len() < 3 {
            return Err(SimError::invalid("incomplete diode model card"));
        }
        let body = tokens[2..].join(" ");
        if !body
            .get(..1)
            .is_some_and(|kind| kind.eq_ignore_ascii_case("d"))
        {
            return Err(SimError::new(
                ErrorCode::Unsupported,
                "bounded importer supports only D and constant R model families",
            ));
        }
        let tail = body[1..].trim();
        if !tail.is_empty()
            && !tail.starts_with('(')
            && !body.chars().nth(1).is_some_and(char::is_whitespace)
        {
            return Err(SimError::new(
                ErrorCode::Unsupported,
                "unsupported bounded model kind",
            ));
        }
        let parameters = if tail.starts_with('(') {
            tail.strip_prefix('(')
                .and_then(|value| value.strip_suffix(')'))
                .ok_or_else(|| SimError::invalid("unbalanced diode model parameters"))?
        } else {
            tail
        };
        if parameters.contains(['(', ')']) {
            return Err(SimError::invalid("nested diode model parameters"));
        }
        let mut values = std::collections::BTreeMap::new();
        for parameter in parameters
            .split(|c: char| c.is_whitespace() || c == ',')
            .filter(|value| !value.is_empty())
        {
            let (key, value) = parameter
                .split_once('=')
                .ok_or_else(|| SimError::invalid("diode parameter requires name=value"))?;
            let key = match key.to_ascii_uppercase().as_str() {
                "CJ0" => "CJO".to_owned(),
                other => other.to_owned(),
            };
            if !matches!(key.as_str(), "IS" | "N" | "CJO" | "M" | "RS" | "TT") {
                return Err(SimError::new(
                    ErrorCode::Unsupported,
                    "unsupported bounded diode model parameter",
                ));
            }
            if values.insert(key, spice_scalar(value)?).is_some() {
                return Err(SimError::invalid("duplicate diode model parameter"));
            }
        }
        let saturation_amps = *values.get("IS").unwrap_or(&1e-14);
        let ideality = *values.get("N").unwrap_or(&1.);
        let thermal_volts = thermal_voltage * ideality;
        crate::model::DiodeModel {
            saturation_amps,
            thermal_volts,
        }
        .validate()?;
        if values.get("RS").is_some_and(|value| *value != 0.)
            || values.get("TT").is_some_and(|value| *value != 0.)
        {
            return Err(SimError::new(
                ErrorCode::Unsupported,
                "nonzero diode series resistance/transport is unsupported",
            ));
        }
        let constant_farads = match (values.get("CJO"), values.get("M")) {
            (None, None) => None,
            (Some(capacitance), Some(grading)) if *grading == 0. && *capacitance >= 0. => {
                if *capacitance == 0. {
                    None
                } else {
                    Some(*capacitance)
                }
            }
            _ => {
                return Err(SimError::new(
                    ErrorCode::Unsupported,
                    "constant diode charge requires explicit nonnegative CJO and M=0",
                ));
            }
        };
        if models
            .insert(
                tokens[1].to_ascii_uppercase(),
                BoundedDiodeModel {
                    saturation_amps,
                    thermal_volts,
                    constant_farads,
                },
            )
            .is_some()
        {
            return Err(SimError::invalid("duplicate bounded diode model"));
        }
    }
    Ok(models)
}
/// Return the one bounded deck temperature in Celsius, or the 300 K nominal.
fn bounded_temperature(lines: &[String]) -> SimResult<f64> {
    let mut celsius = None;
    for line in lines {
        let tokens: Vec<_> = line.split_whitespace().collect();
        if !tokens
            .first()
            .is_some_and(|token| token.eq_ignore_ascii_case(".temp"))
        {
            continue;
        }
        if celsius.is_some() || tokens.len() != 2 {
            return Err(SimError::new(
                ErrorCode::Unsupported,
                "bounded importer supports one one-value .temp directive",
            ));
        }
        celsius = Some(spice_scalar(tokens[1])?);
    }
    let kelvin = celsius.unwrap_or(26.85) + 273.15;
    if !kelvin.is_finite() || kelvin <= 0. {
        return Err(SimError::invalid("invalid bounded deck temperature"));
    }
    // Boltzmann constant divided by elementary charge, in V/K.
    Ok(8.617_333_262_145e-5 * kelvin)
}

// Recognize only the existing constant-source DC/AC shapes. Other auxiliary
// clauses are handled by their explicitly qualified analysis-specific profile.
fn constant_ac_fields(tokens: &[&str]) -> Option<(usize, usize)> {
    if !tokens.first().is_some_and(|name| {
        matches!(
            name.chars().next().map(|kind| kind.to_ascii_uppercase()),
            Some('V' | 'I')
        )
    }) {
        return None;
    }
    if matches!(tokens.len(), 7 | 8)
        && tokens[3].eq_ignore_ascii_case("dc")
        && tokens[5].eq_ignore_ascii_case("ac")
    {
        Some((6, 7))
    } else if matches!(tokens.len(), 6 | 7) && tokens[4].eq_ignore_ascii_case("ac") {
        Some((5, 6))
    } else {
        None
    }
}

// Share validation between active and inactive AC metadata. A is the atto
// scale, not an ampere annotation; only the existing harmless V tail is stripped.
fn bounded_source_phasor(
    tokens: &[&str],
    amplitude_index: usize,
    phase_index: usize,
) -> SimResult<AcSourcePhasor> {
    let amplitude = spice_scalar(tokens[amplitude_index].trim_end_matches(['V', 'v']))?;
    let phase_degrees = if tokens.len() == phase_index + 1 {
        spice_scalar(tokens[phase_index])?
    } else {
        0.
    };
    if !phase_degrees.is_finite() {
        return Err(SimError::invalid("invalid AC source phase"));
    }
    let radians = phase_degrees.to_radians();
    Ok(AcSourcePhasor {
        id: tokens[0].to_ascii_uppercase(),
        real: amplitude * radians.cos(),
        imag: amplitude * radians.sin(),
    })
}

/// Lowers the bounded ngspice deck profile into the same typed request contract
/// used by JSON and SForge callers. Bounded transient optional fields retain
/// zero-start and maximum-step semantics; unsupported analysis requests fail.
pub fn import_ngspice_request(text: &str, case_id: impl Into<String>) -> SimResult<Request> {
    if text.len() > 1_048_576 {
        return Err(SimError::new(
            ErrorCode::ResourceExhausted,
            "ngspice deck exceeds 1 MiB",
        ));
    }
    let mut transient = None;
    let mut ac = None;
    let mut operating_point = false;
    for source_line in text.lines().skip(1) {
        let line = source_line.split(';').next().unwrap_or_default().trim();
        let tokens: Vec<_> = line.split_whitespace().collect();
        if tokens
            .first()
            .is_some_and(|token| token.eq_ignore_ascii_case(".op"))
        {
            if operating_point || tokens.len() != 1 {
                return Err(SimError::invalid("duplicate or malformed .op directive"));
            }
            operating_point = true;
        }
        if tokens
            .first()
            .is_some_and(|token| token.eq_ignore_ascii_case(".tran"))
        {
            if transient.is_some() {
                return Err(SimError::new(
                    ErrorCode::Unsupported,
                    "multiple .tran analyses require a multi-analysis result contract",
                ));
            }
            // UIC is valid ngspice syntax with different initialization
            // semantics, not a malformed numeric start/max-step parameter.
            if tokens
                .iter()
                .skip(3)
                .any(|token| token.eq_ignore_ascii_case("uic"))
            {
                return Err(SimError::new(
                    ErrorCode::Unsupported,
                    "transient UIC initialization is unsupported",
                ));
            }
            if !(3..=5).contains(&tokens.len()) {
                return Err(SimError::new(
                    ErrorCode::Unsupported,
                    "bounded .tran expects step stop [zero-start [max-step]]; UIC is unsupported",
                ));
            }
            let step = spice_scalar(tokens[1])?;
            let stop = spice_scalar(tokens[2])?;
            if step <= 0. || stop <= 0. {
                return Err(SimError::invalid(
                    "transient step and stop must be positive",
                ));
            }
            if tokens.len() >= 4 && spice_scalar(tokens[3])? != 0. {
                return Err(SimError::new(
                    ErrorCode::Unsupported,
                    "nonzero transient output start is unsupported",
                ));
            }
            let maximum_step = if tokens.len() == 5 {
                spice_scalar(tokens[4])?
            } else {
                step
            };
            if maximum_step <= 0. {
                return Err(SimError::invalid("transient maximum step must be positive"));
            }
            transient = Some(crate::configured::TransientConfig {
                max_step_s: maximum_step,
                stop_s: stop,
                ..Default::default()
            });
        }
        if tokens
            .first()
            .is_some_and(|token| token.eq_ignore_ascii_case(".ac"))
        {
            if ac.is_some()
                || tokens.len() != 5
                || !(tokens[1].eq_ignore_ascii_case("lin")
                    || tokens[1].eq_ignore_ascii_case("dec")
                    || tokens[1].eq_ignore_ascii_case("oct"))
            {
                return Err(SimError::new(
                    ErrorCode::Unsupported,
                    "ngspice importer supports exactly one .ac lin, dec or oct count start stop directive",
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
            let linear = tokens[1].eq_ignore_ascii_case("lin");
            if !start.is_finite()
                || !stop.is_finite()
                || start < 0.
                || !linear && start == 0.
                || stop < start
            {
                return Err(SimError::invalid("invalid AC linear frequency range"));
            }
            let frequencies_hz = if linear {
                (0..count)
                    .map(|i| start + (stop - start) * i as f64 / (count - 1).max(1) as f64)
                    .collect()
            } else {
                let units = if tokens[1].eq_ignore_ascii_case("dec") {
                    (stop / start).log10()
                } else {
                    (stop / start).log2()
                };
                let steps = (units * count as f64).floor() as usize;
                if steps > 9_999 {
                    return Err(SimError::invalid("AC decade grid exceeds 10000 points"));
                }
                let radix = if tokens[1].eq_ignore_ascii_case("dec") {
                    10_f64
                } else {
                    2_f64
                };
                let mut grid: Vec<f64> = (0..=steps)
                    .map(|i| start * radix.powf(i as f64 / count as f64))
                    .collect();
                if grid.last().is_none_or(|last| *last < stop) {
                    grid.push(stop);
                }
                grid
            };
            ac = Some(frequencies_hz);
        }
    }
    let analysis = match (transient, ac) {
        (Some(_), Some(_)) => {
            return Err(SimError::new(
                ErrorCode::Unsupported,
                "only one ngspice analysis directive is supported",
            ));
        }
        (Some(options), None) => Analysis::TransientConfigured { options },
        (None, Some(frequencies_hz)) => Analysis::Ac {
            frequencies_hz,
            source_phasors: Vec::new(),
        },
        (None, None) => Analysis::OperatingPoint,
    };
    let mut source_phasors = Vec::new();
    let input = if matches!(analysis, Analysis::Ac { .. }) {
        let mut normalized = String::new();
        for (index, line) in text.lines().enumerate() {
            if index == 0 {
                normalized.push_str(line);
                normalized.push('\n');
                continue;
            }
            let tokens: Vec<_> = line
                .split(';')
                .next()
                .unwrap_or_default()
                .split_whitespace()
                .collect();
            let ac_parameters = if let Some(fields) = constant_ac_fields(&tokens) {
                Some(fields)
            } else if matches!(
                tokens
                    .first()
                    .and_then(|name| name.chars().next())
                    .map(|kind| kind.to_ascii_uppercase()),
                Some('V' | 'I')
            ) && tokens.len() == 17
                && tokens[3].eq_ignore_ascii_case("dc")
                && tokens[5].eq_ignore_ascii_case("ac")
                && tokens[7].eq_ignore_ascii_case("sin")
                && tokens[13].eq_ignore_ascii_case("distof1")
                && tokens[15].eq_ignore_ascii_case("distof2")
                && spice_scalar(tokens[4]).is_ok()
                && spice_scalar(tokens[6]).is_ok()
                && tokens[8..=12]
                    .iter()
                    .all(|value| spice_scalar(value).is_ok())
                && spice_scalar(tokens[14]).is_ok()
                && spice_scalar(tokens[16]).is_ok()
            {
                // The deck requests AC only. Its fully validated SIN/DISTOF
                // tail has no small-signal AC effect and is intentionally not
                // lowered into transient or distortion behavior.
                Some((6, 7))
            } else {
                None
            };
            if let Some((amplitude_index, phase_index)) = ac_parameters {
                source_phasors.push(bounded_source_phasor(
                    &tokens,
                    amplitude_index,
                    phase_index,
                )?);
                normalized.push_str(&format!(
                    "{} {} {} {}\n",
                    tokens[0],
                    tokens[1],
                    tokens[2],
                    // DC bias and small-signal excitation are independent.
                    // Keeping the bias is essential before nonlinear AC linearization.
                    tokens[if tokens[3].eq_ignore_ascii_case("dc") {
                        4
                    } else {
                        3
                    }]
                ));
            } else if tokens.len() == 5
                && tokens.first().is_some_and(|name| {
                    matches!(
                        name.chars().next().map(|kind| kind.to_ascii_uppercase()),
                        Some('R' | 'C' | 'L')
                    )
                })
                && tokens[4]
                    .get(..3)
                    .is_some_and(|prefix| prefix.eq_ignore_ascii_case("ac="))
            {
                normalized.push_str(&format!(
                    "{} {} {} {}\n",
                    tokens[0],
                    tokens[1],
                    tokens[2],
                    &tokens[4][3..]
                ));
            } else {
                normalized.push_str(line);
                normalized.push('\n');
            }
        }
        normalized
    } else {
        text.lines()
            .enumerate()
            .map(|(index, line)| {
                if index == 0 {
                    return Ok(line.to_owned());
                }
                let tokens: Vec<_> = line
                    .split(';')
                    .next()
                    .unwrap_or_default()
                    .split_whitespace()
                    .collect();
                let prepared =
                    if let Some((amplitude_index, phase_index)) = constant_ac_fields(&tokens) {
                        // Validate inactive excitation but preserve only the DC
                        // value in the constant OP/transient circuit.
                        bounded_source_phasor(&tokens, amplitude_index, phase_index)?;
                        let dc_index = if tokens[3].eq_ignore_ascii_case("dc") {
                            4
                        } else {
                            3
                        };
                        format!(
                            "{} {} {} {}",
                            tokens[0], tokens[1], tokens[2], tokens[dc_index]
                        )
                    } else if (matches!(tokens.len(), 7 | 9 | 10)
                        && tokens[3].eq_ignore_ascii_case("sin"))
                        || (tokens.len() == 10 && tokens[3].eq_ignore_ascii_case("exp"))
                    {
                        format!("{} {} {} {}", tokens[0], tokens[1], tokens[2], tokens[4])
                    } else if tokens.len() >= 6
                        && tokens[3].eq_ignore_ascii_case("pwl")
                        && (tokens.len() - 4).is_multiple_of(2)
                    {
                        format!("{} {} {} {}", tokens[0], tokens[1], tokens[2], tokens[5])
                    } else if tokens.len() == 4
                        && (tokens[3]
                            .get(..4)
                            .is_some_and(|prefix| prefix.eq_ignore_ascii_case("sin("))
                            || tokens[3]
                                .get(..6)
                                .is_some_and(|prefix| prefix.eq_ignore_ascii_case("pulse("))
                            || tokens[3]
                                .get(..4)
                                .is_some_and(|prefix| prefix.eq_ignore_ascii_case("pwl("))
                            || tokens[3]
                                .get(..4)
                                .is_some_and(|prefix| prefix.eq_ignore_ascii_case("exp(")))
                        && tokens[3].ends_with(')')
                    {
                        let start = if tokens[3]
                            .get(..6)
                            .is_some_and(|prefix| prefix.eq_ignore_ascii_case("pulse("))
                        {
                            6
                        } else {
                            4
                        };
                        let offset = tokens[3][start..tokens[3].len() - 1]
                            .split(',')
                            .next()
                            .unwrap_or_default();
                        format!("{} {} {} {}", tokens[0], tokens[1], tokens[2], offset)
                    } else {
                        line.into()
                    };
                Ok(prepared)
            })
            .collect::<SimResult<Vec<String>>>()?
            .join("\n")
    };
    let mut sources = Vec::new();
    for source_line in text.lines().skip(1) {
        let line = source_line.split(';').next().unwrap_or_default().trim();
        let tokens: Vec<_> = line.split_whitespace().collect();
        let Some(name) = tokens.first() else { continue };
        if !matches!(
            name.chars().next().map(|c| c.to_ascii_uppercase()),
            Some('V' | 'I')
        ) {
            continue;
        }
        if tokens.len() == 11 && tokens[3].eq_ignore_ascii_case("pulse") {
            let values: SimResult<Vec<f64>> = tokens[4..]
                .iter()
                .map(|value| spice_scalar(value))
                .collect();
            let values = values?;
            sources.push(SourceOverride {
                id: name.to_ascii_uppercase(),
                waveform: Waveform::Pulse {
                    low: values[0],
                    high: values[1],
                    delay_s: values[2],
                    rise_s: values[3],
                    fall_s: values[4],
                    width_s: values[5],
                    period_s: values[6],
                },
            });
        }
        if tokens.len() == 4
            && tokens[3]
                .get(..6)
                .is_some_and(|prefix| prefix.eq_ignore_ascii_case("pulse("))
            && tokens[3].ends_with(')')
        {
            let values: SimResult<Vec<f64>> = tokens[3][6..tokens[3].len() - 1]
                .split(',')
                .map(spice_scalar)
                .collect();
            let values = values?;
            if values.len() != 7 {
                return Err(SimError::new(
                    ErrorCode::Unsupported,
                    "bounded PULSE source requires seven values",
                ));
            }
            sources.push(SourceOverride {
                id: name.to_ascii_uppercase(),
                waveform: Waveform::Pulse {
                    low: values[0],
                    high: values[1],
                    delay_s: values[2],
                    rise_s: values[3],
                    fall_s: values[4],
                    width_s: values[5],
                    period_s: values[6],
                },
            });
        }
        if tokens.len() == 4
            && tokens[3]
                .get(..4)
                .is_some_and(|prefix| prefix.eq_ignore_ascii_case("sin("))
            && tokens[3].ends_with(')')
        {
            let values: SimResult<Vec<f64>> = tokens[3][4..tokens[3].len() - 1]
                .split(',')
                .map(spice_scalar)
                .collect();
            let values = values?;
            sources.push(SourceOverride {
                id: name.to_ascii_uppercase(),
                waveform: bounded_sine(values)?,
            });
        }
        if matches!(tokens.len(), 7 | 9 | 10) && tokens[3].eq_ignore_ascii_case("sin") {
            let values: SimResult<Vec<f64>> = tokens[4..]
                .iter()
                .map(|value| spice_scalar(value))
                .collect();
            let values = values?;
            sources.push(SourceOverride {
                id: name.to_ascii_uppercase(),
                waveform: bounded_sine(values)?,
            });
        }
        if tokens.len() == 4
            && tokens[3]
                .get(..4)
                .is_some_and(|prefix| prefix.eq_ignore_ascii_case("pwl("))
            && tokens[3].ends_with(')')
        {
            let values: SimResult<Vec<f64>> = tokens[3][4..tokens[3].len() - 1]
                .split(',')
                .map(spice_scalar)
                .collect();
            let values = values?;
            if values.len() < 2 || values.len() % 2 != 0 {
                return Err(SimError::new(
                    ErrorCode::Unsupported,
                    "bounded PWL source requires time,value pairs",
                ));
            }
            sources.push(SourceOverride {
                id: name.to_ascii_uppercase(),
                waveform: Waveform::Pwl {
                    points: values
                        .chunks_exact(2)
                        .map(|pair| Point {
                            time_s: pair[0],
                            value: pair[1],
                        })
                        .collect(),
                },
            });
        }
        if tokens.len() >= 6
            && tokens[3].eq_ignore_ascii_case("pwl")
            && (tokens.len() - 4).is_multiple_of(2)
        {
            let values: SimResult<Vec<f64>> = tokens[4..]
                .iter()
                .map(|value| spice_scalar(value))
                .collect();
            let values = values?;
            sources.push(SourceOverride {
                id: name.to_ascii_uppercase(),
                waveform: Waveform::Pwl {
                    points: values
                        .chunks_exact(2)
                        .map(|pair| Point {
                            time_s: pair[0],
                            value: pair[1],
                        })
                        .collect(),
                },
            });
        }
        if tokens.len() == 4
            && tokens[3]
                .get(..4)
                .is_some_and(|prefix| prefix.eq_ignore_ascii_case("exp("))
            && tokens[3].ends_with(')')
        {
            let values: SimResult<Vec<f64>> = tokens[3][4..tokens[3].len() - 1]
                .split(',')
                .map(spice_scalar)
                .collect();
            let values = values?;
            if values.len() != 6 {
                return Err(SimError::new(
                    ErrorCode::Unsupported,
                    "bounded EXP source requires six values",
                ));
            }
            sources.push(SourceOverride {
                id: name.to_ascii_uppercase(),
                waveform: Waveform::Exponential {
                    initial: values[0],
                    pulsed: values[1],
                    rise_delay_s: values[2],
                    rise_tau_s: values[3],
                    fall_delay_s: values[4],
                    fall_tau_s: values[5],
                },
            });
        }
        if tokens.len() == 10 && tokens[3].eq_ignore_ascii_case("exp") {
            let values: SimResult<Vec<f64>> = tokens[4..]
                .iter()
                .map(|value| spice_scalar(value))
                .collect();
            let values = values?;
            sources.push(SourceOverride {
                id: name.to_ascii_uppercase(),
                waveform: Waveform::Exponential {
                    initial: values[0],
                    pulsed: values[1],
                    rise_delay_s: values[2],
                    rise_tau_s: values[3],
                    fall_delay_s: values[4],
                    fall_tau_s: values[5],
                },
            });
        }
    }
    let circuit = parse_ngspice_basic(&input)?;
    if matches!(analysis, Analysis::Ac { .. }) {
        // SPICE sources without an AC clause contribute bias only. The raw typed
        // API retains its separate historical nominal-amplitude convention.
        for element in &circuit.elements {
            match element {
                crate::circuit::Element::Voltage { id, .. }
                | crate::circuit::Element::Current { id, .. }
                    if !source_phasors.iter().any(|source| source.id == *id) =>
                {
                    source_phasors.push(AcSourcePhasor {
                        id: id.clone(),
                        real: 0.,
                        imag: 0.,
                    });
                }
                _ => {}
            }
        }
    }
    let analysis = match analysis {
        Analysis::TransientConfigured { mut options } => {
            options.sources = sources;
            Analysis::TransientConfigured { options }
        }
        _ if !sources.is_empty() => {
            return Err(SimError::new(
                ErrorCode::Unsupported,
                "PULSE sources require a bounded .tran directive",
            ));
        }
        Analysis::Ac { frequencies_hz, .. } => Analysis::Ac {
            frequencies_hz,
            source_phasors,
        },
        other => other,
    };
    Ok(Request {
        schema_version: "1".into(),
        case_id: case_id.into(),
        circuit,
        analysis,
        backend: "lu".into(),
        experiment: None,
    })
}

/// Lower the bounded SPICE SIN parameter subsets without guessing omitted terms.
fn bounded_sine(values: Vec<f64>) -> SimResult<Waveform> {
    match values.as_slice() {
        [offset, amplitude, frequency_hz] => Ok(Waveform::Sine {
            offset: *offset,
            amplitude: *amplitude,
            frequency_hz: *frequency_hz,
            phase_rad: 0.,
        }),
        [offset, amplitude, frequency_hz, delay_s, damping_per_s] => Ok(Waveform::DampedSine {
            offset: *offset,
            amplitude: *amplitude,
            frequency_hz: *frequency_hz,
            delay_s: *delay_s,
            damping_per_s: *damping_per_s,
            phase_rad: 0.,
        }),
        [
            offset,
            amplitude,
            frequency_hz,
            delay_s,
            damping_per_s,
            phase_degrees,
        ] => Ok(Waveform::DampedSine {
            offset: *offset,
            amplitude: *amplitude,
            frequency_hz: *frequency_hz,
            delay_s: *delay_s,
            damping_per_s: *damping_per_s,
            phase_rad: phase_degrees.to_radians(),
        }),
        _ => Err(SimError::new(
            ErrorCode::Unsupported,
            "bounded SIN source requires 3, 5, or 6 values",
        )),
    }
}
