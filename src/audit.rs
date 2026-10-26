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
    /// Record a capability or accuracy claim for which this run has no evidence.
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
    /// Reject nonpositive or nonfinite dimensional and relative bounds.
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
    /// Unavailable checks do not constitute a failure or establish a pass.
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
    checks.extend(constraint_checks(circuit, state, options)?);
    checks.push(power_check(circuit, state, &currents, options, gmin));
    if gmin.is_some() {
        checks.push(Check::not_checked(
            "unregularized_physics",
            "A",
            "explicit fixed Gmin is included in the effective circuit",
        ));
    }
    Ok(AuditReport { checks })
}

/// Check voltage-defined source equations separately from node conservation.
fn constraint_checks(
    circuit: &Circuit,
    state: &[f64],
    options: AuditOptions,
) -> SimResult<Vec<Check>> {
    let mut checks = Vec::new();
    for element in &circuit.elements {
        let (p, n) = element.terminals();
        let actual = voltage(state, p) - voltage(state, n);
        let expected = match element {
            Element::Voltage { volts, .. } => *volts,
            Element::Inductor { .. } => 0.,
            Element::Vcvs { cp, cn, gain, .. } => {
                gain * (voltage(state, *cp) - voltage(state, *cn))
            }
            Element::Ccvs {
                control_id, ohms, ..
            } => ohms * branch_current(circuit, state, control_id)?,
            _ => continue,
        };
        checks.push(Check::measured(
            format!("constraint:{}", element.id()),
            actual - expected,
            options.voltage_absolute_v + options.relative * (actual.abs() + expected.abs()),
            "V",
        ));
    }
    Ok(checks)
}

/// Passive sign convention: supplying sources contribute negative absorbed power.
fn power_check(
    circuit: &Circuit,
    state: &[f64],
    currents: &[f64],
    options: AuditOptions,
    gmin: Option<f64>,
) -> Check {
    let mut sum = 0.;
    let mut scale = 0.;
    for (element, current) in circuit.elements.iter().zip(currents) {
        let (p, n) = element.terminals();
        let power = (voltage(state, p) - voltage(state, n)) * current;
        sum += power;
        scale += power.abs();
    }
    for value in state.iter().take(circuit.nodes.len() - 1) {
        let power = gmin.unwrap_or(0.) * value * value;
        sum += power;
        scale += power.abs();
    }
    Check::measured(
        "power_balance",
        sum,
        options.power_absolute_w + options.relative * scale,
        "W",
    )
}

/// Audit one accepted backward-Euler step with constant nominal sources. The
/// discrete identity includes BE numerical dissipation, 0.5*C*deltaV^2 and
/// 0.5*L*deltaI^2, rather than incorrectly requiring exact continuous energy.
pub fn backward_euler_step(
    circuit: &Circuit,
    previous: &[f64],
    state: &[f64],
    step_s: f64,
    options: AuditOptions,
) -> SimResult<AuditReport> {
    options.validate()?;
    validate_state(circuit, previous)?;
    validate_state(circuit, state)?;
    if !step_s.is_finite() || step_s <= 0. {
        return Err(SimError::invalid("invalid audit timestep"));
    }
    let mut currents = dc_currents(circuit, state)?;
    let mut energy = 0.;
    let mut energy_scale = 0.;
    let mut checks = constraint_checks(circuit, state, options)?;
    for (element, current) in circuit.elements.iter().zip(&mut currents) {
        let (p, n) = element.terminals();
        let v = voltage(state, p) - voltage(state, n);
        let old_v = voltage(previous, p) - voltage(previous, n);
        let term = match element {
            Element::Capacitor { farads, .. } => {
                *current = farads * (v - old_v) / step_s;
                farads * v * (v - old_v)
            }
            Element::Inductor { henries, .. } => {
                let old_i = branch_current(circuit, previous, element.id())?;
                let expected = henries * (*current - old_i) / step_s;
                checks.retain(|c| c.id != format!("constraint:{}", element.id()));
                checks.push(Check::measured(
                    format!("constraint:{}", element.id()),
                    v - expected,
                    options.voltage_absolute_v + options.relative * (v.abs() + expected.abs()),
                    "V",
                ));
                henries * *current * (*current - old_i)
            }
            _ => step_s * v * *current,
        };
        energy += term;
        energy_scale += term.abs();
    }
    checks.extend(kcl_checks(circuit, state, &currents, options, None));
    checks.push(Check::measured(
        "be_energy_balance",
        energy,
        options.energy_absolute_j + options.relative * energy_scale,
        "J",
    ));
    Ok(AuditReport { checks })
}

impl AuditReport {
    /// Retain the worst normalized discrepancy per identifier across a waveform.
    /// Space is bounded by circuit size rather than sample count.
    pub fn merge_worst(&mut self, other: Self) {
        fn severity(c: &Check) -> f64 {
            if c.status == CheckStatus::Failed && (c.discrepancy.is_none() || c.tolerance.is_none())
            {
                return f64::INFINITY;
            }
            match (c.discrepancy, c.tolerance) {
                (Some(d), Some(t)) => d.abs() / t.max(f64::MIN_POSITIVE),
                _ => -1.,
            }
        }
        for check in other.checks {
            if let Some(existing) = self.checks.iter_mut().find(|c| c.id == check.id) {
                if severity(&check) > severity(existing) {
                    *existing = check;
                }
            } else {
                self.checks.push(check);
            }
        }
    }
}

/// Compare an explicitly rerun finer waveform at exactly shared coarse sample
/// times. This reports observed change, not a rigorous global error estimate.
/// No interpolation, extrapolation, solver rerun or hidden resource expansion occurs.
pub fn compare_refinement(
    coarse: &[crate::transient::Sample],
    fine: &[crate::transient::Sample],
    signal: usize,
    absolute: f64,
    relative: f64,
    unit: &'static str,
) -> SimResult<Check> {
    if !absolute.is_finite()
        || absolute <= 0.
        || !relative.is_finite()
        || relative <= 0.
        || !["V", "A"].contains(&unit)
        || coarse.is_empty()
        || fine.len() <= coarse.len()
        || fine.len() > 100_000
    {
        return Err(SimError::invalid("invalid refinement comparison"));
    }
    for samples in [coarse, fine] {
        if samples.iter().any(|s| {
            !s.time_s.is_finite()
                || s.time_s < 0.
                || signal >= s.values.len()
                || !s.values[signal].is_finite()
        }) || samples.windows(2).any(|w| w[0].time_s >= w[1].time_s)
        {
            return Err(SimError::invalid(
                "invalid refinement axis or selected signal",
            ));
        }
    }
    if coarse.last().unwrap().time_s != fine.last().unwrap().time_s {
        return Err(SimError::invalid("refinement stop times differ"));
    }
    let mut report = AuditReport { checks: Vec::new() };
    for sample in coarse {
        let index = fine
            .binary_search_by(|s| s.time_s.total_cmp(&sample.time_s))
            .map_err(|_| {
                SimError::invalid("refinement requires exactly shared coarse sample times")
            })?;
        let (a, b) = (sample.values[signal], fine[index].values[signal]);
        let mut check = Check::measured(
            "timestep_refinement",
            a - b,
            absolute + relative * a.abs().max(b.abs()),
            unit,
        );
        check.reason = Some(format!(
            "observed change at time_s={}; not a global error bound",
            sample.time_s
        ));
        report.merge_worst(AuditReport {
            checks: vec![check],
        });
    }
    Ok(report.checks.remove(0))
}
