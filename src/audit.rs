//! Independent physical checks over circuit elements and returned MNA states.
//! No audit reads assembled matrices or calls a numerical solver.
use crate::error::{SimError, SimResult};
use serde::Serialize;

/// Explicit check outcome; unavailable evidence is never presented as a pass.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize)]
#[serde(rename_all = "snake_case")]
pub enum CheckStatus {
    Passed,
    Failed,
    NotChecked,
}

/// A discrepancy in SI units and its effective absolute-plus-relative bound.
#[derive(Debug, Clone, Serialize)]
pub struct Check {
    pub id: String,
    pub status: CheckStatus,
    pub discrepancy: Option<f64>,
    pub tolerance: Option<f64>,
    pub unit: &'static str,
    pub reason: Option<String>,
}
impl Check {
    /// Overflow is a failed check with null metrics, preserving valid JSON.
    pub fn measured(
        id: impl Into<String>,
        discrepancy: f64,
        tolerance: f64,
        unit: &'static str,
    ) -> Self {
        let finite = discrepancy.is_finite() && tolerance.is_finite() && tolerance >= 0.;
        Self {
            id: id.into(),
            status: if finite && discrepancy.abs() <= tolerance {
                CheckStatus::Passed
            } else {
                CheckStatus::Failed
            },
            discrepancy: discrepancy.is_finite().then_some(discrepancy),
            tolerance: (tolerance.is_finite() && tolerance >= 0.).then_some(tolerance),
            unit,
            reason: (!finite).then(|| "nonfinite discrepancy or invalid bound".into()),
        }
    }
    pub fn not_checked(id: impl Into<String>, unit: &'static str, reason: &str) -> Self {
        Self {
            id: id.into(),
            status: CheckStatus::NotChecked,
            discrepancy: None,
            tolerance: None,
            unit,
            reason: Some(reason.into()),
        }
    }
}

/// Dimensional bounds; relative scaling uses sums of absolute physical terms.
#[derive(Debug, Clone, Copy)]
pub struct AuditOptions {
    pub current_absolute_a: f64,
    pub voltage_absolute_v: f64,
    pub power_absolute_w: f64,
    pub energy_absolute_j: f64,
    pub relative: f64,
}
impl Default for AuditOptions {
    fn default() -> Self {
        Self {
            current_absolute_a: 1e-10,
            voltage_absolute_v: 1e-8,
            power_absolute_w: 1e-10,
            energy_absolute_j: 1e-12,
            relative: 1e-8,
        }
    }
}
impl AuditOptions {
    pub fn validate(self) -> SimResult<()> {
        if [
            self.current_absolute_a,
            self.voltage_absolute_v,
            self.power_absolute_w,
            self.energy_absolute_j,
            self.relative,
        ]
        .iter()
        .any(|v| !v.is_finite() || *v <= 0.)
        {
            return Err(SimError::invalid("invalid audit tolerance"));
        }
        Ok(())
    }
}

/// Persistent evidence, including checks deliberately outside the supported profile.
#[derive(Debug, Clone, Serialize)]
pub struct AuditReport {
    pub checks: Vec<Check>,
}
impl AuditReport {
    pub fn has_failures(&self) -> bool {
        self.checks.iter().any(|c| c.status == CheckStatus::Failed)
    }
}

use crate::circuit::{Circuit, Element};

/// The branch ordering is the public MNA convention: node voltages first, then
/// voltage-defined branches in element order. Validate before indexing any state.
fn branch_ids(circuit: &Circuit) -> Vec<&str> {
    circuit
        .elements
        .iter()
        .filter_map(|e| match e {
            Element::Voltage { id, .. }
            | Element::Inductor { id, .. }
            | Element::Vcvs { id, .. }
            | Element::Ccvs { id, .. } => Some(id.as_str()),
            _ => None,
        })
        .collect()
}
fn validate_state(circuit: &Circuit, state: &[f64]) -> SimResult<()> {
    circuit.validate()?;
    if state.len() != circuit.nodes.len() - 1 + branch_ids(circuit).len()
        || state.iter().any(|v| !v.is_finite())
    {
        return Err(SimError::invalid(
            "audit requires a finite complete MNA state",
        ));
    }
    Ok(())
}
fn voltage(state: &[f64], node: crate::ids::NodeId) -> f64 {
    node.unknown().map(|i| state[i]).unwrap_or(0.)
}
fn branch_current(circuit: &Circuit, state: &[f64], id: &str) -> SimResult<f64> {
    let index = branch_ids(circuit)
        .iter()
        .position(|name| *name == id)
        .ok_or_else(|| SimError::invalid(format!("audit missing branch {id}")))?;
    Ok(state[circuit.nodes.len() - 1 + index])
}
/// Element equations reconstructed directly, including p-to-n source polarity.
fn dc_currents(circuit: &Circuit, state: &[f64]) -> SimResult<Vec<f64>> {
    circuit
        .elements
        .iter()
        .map(|e| {
            let (p, n) = e.terminals();
            let v = voltage(state, p) - voltage(state, n);
            let current = match e {
                Element::Resistor { ohms, .. } => v / ohms,
                Element::Capacitor { .. } => 0.,
                Element::Current { amps, .. } => *amps,
                Element::Diode {
                    saturation_amps,
                    thermal_volts,
                    ..
                } => saturation_amps * (v / thermal_volts).exp_m1(),
                Element::Vccs {
                    cp, cn, siemens, ..
                } => siemens * (voltage(state, *cp) - voltage(state, *cn)),
                Element::Cccs {
                    control_id, gain, ..
                } => gain * branch_current(circuit, state, control_id)?,
                _ => branch_current(circuit, state, e.id())?,
            };
            if !current.is_finite() {
                return Err(SimError::new(
                    crate::error::ErrorCode::NumericalFailure,
                    "audit device current overflow",
                ));
            }
            Ok(current)
        })
        .collect()
}
fn kcl_checks(
    circuit: &Circuit,
    state: &[f64],
    currents: &[f64],
    options: AuditOptions,
    gmin: Option<f64>,
) -> Vec<Check> {
    let mut sums = vec![0.; circuit.nodes.len()];
    let mut scales = sums.clone();
    for (element, current) in circuit.elements.iter().zip(currents) {
        let (p, n) = element.terminals();
        sums[p.0] += current;
        sums[n.0] -= current;
        scales[p.0] += current.abs();
        scales[n.0] += current.abs();
    }
    // Include the reference node: its balance is useful even though MNA omits its row.
    for node in 1..circuit.nodes.len() {
        let shunt = gmin.unwrap_or(0.) * state[node - 1];
        sums[node] += shunt;
        sums[0] -= shunt;
        scales[node] += shunt.abs();
        scales[0] += shunt.abs();
    }
    sums.iter()
        .enumerate()
        .map(|(i, value)| {
            Check::measured(
                format!("kcl:{}", circuit.nodes[i]),
                *value,
                options.current_absolute_a + options.relative * scales[i],
                "A",
            )
        })
        .collect()
}

/// Audit a DC state independently of assembly and solver convergence. Explicit
/// fixed Gmin is audited as part of the effective circuit, never silently omitted.
pub fn dc(
    circuit: &Circuit,
    state: &[f64],
    options: AuditOptions,
    gmin: Option<f64>,
) -> SimResult<AuditReport> {
    options.validate()?;
    validate_state(circuit, state)?;
    if gmin.is_some_and(|g| !g.is_finite() || g <= 0. || g > 1.) {
        return Err(SimError::invalid("invalid audit Gmin"));
    }
    let currents = dc_currents(circuit, state)?;
    let mut checks = kcl_checks(circuit, state, &currents, options, gmin);
    if gmin.is_some() {
        checks.push(Check::not_checked(
            "unregularized_physics",
            "A",
            "explicit fixed Gmin is included in the effective circuit",
        ));
    }
    Ok(AuditReport { checks })
}
