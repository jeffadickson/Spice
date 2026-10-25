use crate::{
    error::{ErrorCode, SimError, SimResult},
    ids::NodeId,
};
use serde::{Deserialize, Serialize};
use std::collections::BTreeSet;
/// SI terminal conventions: currents flow p -> n, including source and inductor branches.
#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(tag = "kind", rename_all = "snake_case", deny_unknown_fields)]
pub enum Element {
    Resistor {
        id: String,
        p: NodeId,
        n: NodeId,
        ohms: f64,
    },
    Capacitor {
        id: String,
        p: NodeId,
        n: NodeId,
        farads: f64,
    },
    Inductor {
        id: String,
        p: NodeId,
        n: NodeId,
        henries: f64,
    },
    Voltage {
        id: String,
        p: NodeId,
        n: NodeId,
        volts: f64,
    },
    Current {
        id: String,
        p: NodeId,
        n: NodeId,
        amps: f64,
    },
    /// Shockley diode with current defined from p to n.
    Diode {
        id: String,
        p: NodeId,
        n: NodeId,
        saturation_amps: f64,
        thermal_volts: f64,
    },
}
impl Element {
    pub fn id(&self) -> &str {
        match self {
            Self::Resistor { id, .. }
            | Self::Capacitor { id, .. }
            | Self::Inductor { id, .. }
            | Self::Voltage { id, .. }
            | Self::Current { id, .. }
            | Self::Diode { id, .. } => id,
        }
    }
    pub fn terminals(&self) -> (NodeId, NodeId) {
        match self {
            Self::Resistor { p, n, .. }
            | Self::Capacitor { p, n, .. }
            | Self::Inductor { p, n, .. }
            | Self::Voltage { p, n, .. }
            | Self::Current { p, n, .. }
            | Self::Diode { p, n, .. } => (*p, *n),
        }
    }
}
/// Explicit ground and stable ordered node identities; no mutable compiled-plan backdoor.
#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(deny_unknown_fields)]
pub struct Circuit {
    pub nodes: Vec<String>,
    pub elements: Vec<Element>,
}
impl Circuit {
    pub fn validate(&self) -> SimResult<()> {
        if self.nodes.first().map(String::as_str) != Some("0") {
            return Err(SimError::invalid("nodes[0] must be ground 0"));
        }
        if self.nodes.len() > 128 || self.elements.len() > 1024 {
            return Err(SimError::new(
                ErrorCode::ResourceExhausted,
                "reference circuit limit exceeded",
            ));
        }
        let mut names = BTreeSet::new();
        for name in &self.nodes {
            if name.is_empty() || name.len() > 256 || !names.insert(name) {
                return Err(SimError::invalid(
                    "node names must be nonempty, unique and at most 256 bytes",
                ));
            }
        }
        let mut ids = BTreeSet::new();
        for e in &self.elements {
            if e.id().is_empty() || e.id().len() > 256 || !ids.insert(e.id()) {
                return Err(SimError::invalid(
                    "element IDs must be nonempty, unique and at most 256 bytes",
                ));
            }
            let (p, n) = e.terminals();
            if p == n || p.0 >= self.nodes.len() || n.0 >= self.nodes.len() {
                return Err(SimError::invalid(format!(
                    "invalid terminals on {}",
                    e.id()
                )));
            }
            match e {
                Element::Resistor { ohms, .. } if !ohms.is_finite() || *ohms <= 0.0 => {
                    return Err(SimError::invalid("resistance must be finite and positive"));
                }
                Element::Capacitor { farads, .. } if !farads.is_finite() || *farads <= 0.0 => {
                    return Err(SimError::invalid("capacitance must be finite and positive"));
                }
                Element::Inductor { henries, .. } if !henries.is_finite() || *henries <= 0.0 => {
                    return Err(SimError::invalid("inductance must be finite and positive"));
                }
                Element::Voltage { volts, .. } if !volts.is_finite() => {
                    return Err(SimError::invalid("voltage must be finite"));
                }
                Element::Current { amps, .. } if !amps.is_finite() => {
                    return Err(SimError::invalid("current must be finite"));
                }
                Element::Diode {
                    saturation_amps,
                    thermal_volts,
                    ..
                } if !saturation_amps.is_finite()
                    || !thermal_volts.is_finite()
                    || *saturation_amps <= 0.
                    || *thermal_volts <= 0. =>
                {
                    return Err(SimError::invalid(
                        "diode parameters must be finite and positive",
                    ));
                }
                _ => {}
            }
        }
        Ok(())
    }
}
