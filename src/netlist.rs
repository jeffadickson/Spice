use crate::{
    circuit::{Circuit, Element},
    error::{ErrorCode, SimError, SimResult},
    ids::NodeId,
    quantity::spice_scalar,
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
        if !matches!(kind, 'R' | 'C' | 'L' | 'D' | 'V' | 'I') {
            return Err(SimError::new(
                ErrorCode::Unsupported,
                format!("line {}: unsupported element/directive", index + 1),
            ));
        }
        let expected = if kind == 'D' { 5 } else { 4 };
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
        let value = spice_scalar(tokens[3])
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
