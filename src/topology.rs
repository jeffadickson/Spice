//! Bounded preflight diagnostics. Connectivity is advisory for controlled networks.
use crate::{
    circuit::{Circuit, Element},
    error::SimResult,
};
use serde::Serialize;

/// Stable diagnostic code plus an affected identifier; no solver state is exposed.
#[derive(Debug, Clone, Serialize)]
pub struct Diagnostic {
    pub code: &'static str,
    pub severity: &'static str,
    pub subject: String,
    pub message: String,
}

/// Recognize missing DC reference paths and directly conflicting parallel ideal
/// voltage sources. This is not a full rank, loop or DAE-index analysis.
pub fn inspect(circuit: &Circuit) -> SimResult<Vec<Diagnostic>> {
    circuit.validate()?;
    let mut edges = vec![Vec::new(); circuit.nodes.len()];
    let mut sources = std::collections::BTreeMap::new();
    let mut result = Vec::new();
    for element in &circuit.elements {
        let (p, n) = element.terminals();
        if !matches!(
            element,
            Element::Bsim330 { .. }
                | Element::BsimSoiFdReference { .. }
                | Element::Mos1Channel { .. }
                | Element::Capacitor { .. }
                | Element::JunctionStorage { .. }
                | Element::Current { .. }
                | Element::Vccs { .. }
                | Element::Cccs { .. }
        ) {
            edges[p.0].push(n.0);
            edges[n.0].push(p.0);
        }
        if let Element::Voltage { volts, id, .. } = element {
            let (key, value) = if p.0 < n.0 {
                ((p.0, n.0), *volts)
            } else {
                ((n.0, p.0), -*volts)
            };
            if let Some(previous) = sources.insert(key, value) {
                if previous != value {
                    result.push(Diagnostic {
                        code: "conflicting_parallel_voltage_sources",
                        severity: "error",
                        subject: id.clone(),
                        message:
                            "parallel ideal voltage sources prescribe different terminal voltages"
                                .into(),
                    });
                }
            }
        }
    }
    let mut reached = vec![false; circuit.nodes.len()];
    let mut pending = vec![0];
    reached[0] = true;
    while let Some(node) = pending.pop() {
        for &next in &edges[node] {
            if !reached[next] {
                reached[next] = true;
                pending.push(next);
            }
        }
    }
    for (index, found) in reached.iter().enumerate().skip(1) {
        if !found {
            result.push(Diagnostic { code:"no_obvious_dc_reference_path",severity:"warning",subject:circuit.nodes[index].clone(),message:"no reference path through the bounded DC connectivity graph; controlled-source rank and explicit transient initialization require separate analysis".into() });
        }
    }
    Ok(result)
}
