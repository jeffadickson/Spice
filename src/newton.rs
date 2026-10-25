//! Residual/Jacobian assembly and bounded damped Newton operating-point solves.

use crate::{compile::CompiledCircuit, error::{ErrorCode, SimError, SimResult}, linalg::{LinearSolver, Matrix}};

/// Residual F(x) and analytic Jacobian dF/dx at one immutable circuit trial.
#[derive(Debug, Clone)]
pub struct LinearizedSystem { pub residual: Vec<f64>, pub jacobian: Matrix }

fn voltage(values: &[f64], p: crate::ids::NodeId, n: crate::ids::NodeId) -> f64 {
    p.unknown().map(|index| values[index]).unwrap_or(0.) - n.unknown().map(|index| values[index]).unwrap_or(0.)
}

fn stamp_vector(values: &mut [f64], p: crate::ids::NodeId, n: crate::ids::NodeId, current: f64) {
    if let Some(index) = p.unknown() { values[index] += current; }
    if let Some(index) = n.unknown() { values[index] -= current; }
}

/// Assemble the MNA residual and analytic diode Jacobian at `values`.
pub fn linearize(plan: &CompiledCircuit, values: &[f64]) -> SimResult<LinearizedSystem> {
    if values.len() != plan.unknown_count() || values.iter().any(|value| !value.is_finite()) {
        return Err(SimError::invalid("invalid Newton trial vector"));
    }
    let mut residual = vec![0.; values.len()];
    for (row, residual_value) in residual.iter_mut().enumerate() {
        *residual_value = (0..values.len()).map(|column| plan.conductance.at(row, column) * values[column]).sum::<f64>() - plan.rhs[row];
    }
    let mut jacobian = plan.conductance.clone();
    for diode in &plan.diodes {
        let contribution = diode.model.linearize(voltage(values, diode.p, diode.n))?;
        stamp_vector(&mut residual, diode.p, diode.n, contribution.current_amps);
        crate::compile::stamp_pair(&mut jacobian, diode.p, diode.n, contribution.conductance_siemens);
    }
    if residual.iter().any(|value| !value.is_finite()) { return Err(SimError::invalid("nonfinite Newton residual")); }
    Ok(LinearizedSystem { residual, jacobian })
}

/// Infinity norm used for scale-independent Newton convergence and line search.
pub fn norm_inf(values: &[f64]) -> SimResult<f64> {
    let norm = values.iter().fold(0_f64, |worst, value| worst.max(value.abs()));
    if !norm.is_finite() { return Err(SimError::invalid("nonfinite Newton norm")); }
    Ok(norm)
}

/// Bounded Newton policy for the reference diode operating-point solver.
#[derive(Debug, Clone, Copy, serde::Serialize, serde::Deserialize)]
#[serde(deny_unknown_fields)]
pub struct NewtonOptions {
    pub max_iterations: usize,
    pub absolute_tolerance: f64,
    pub relative_tolerance: f64,
}

impl Default for NewtonOptions {
    fn default() -> Self { Self { max_iterations: 50, absolute_tolerance: 1e-12, relative_tolerance: 1e-9 } }
}

impl NewtonOptions {
    pub fn validate(self) -> SimResult<()> {
        if self.max_iterations == 0 || self.max_iterations > 256 || !self.absolute_tolerance.is_finite() || !self.relative_tolerance.is_finite() || self.absolute_tolerance <= 0. || self.relative_tolerance <= 0. {
            return Err(SimError::invalid("invalid Newton options"));
        }
        Ok(())
    }
}

/// Deterministic convergence evidence for an accepted operating point.
#[derive(Debug, Clone, serde::Serialize)]
pub struct NewtonReport { pub iterations: usize, pub residual_inf: f64, pub damped_steps: usize }

fn limited_candidate(plan: &CompiledCircuit, current: &[f64], delta: &[f64], alpha: f64) -> (Vec<f64>, bool) {
    let mut scale = alpha;
    for diode in &plan.diodes {
        let old = voltage(current, diode.p, diode.n);
        let change = voltage(delta, diode.p, diode.n);
        // A bounded 0.5 V terminal change avoids exponential overflow while
        // preserving a single scalar damping factor for the full MNA system.
        if change.abs() > 0.5 { scale = scale.min(0.5 / change.abs()); }
        if old + scale * change > 17. { scale = scale.min((17. - old) / change); }
    }
    (current.iter().zip(delta).map(|(x, d)| x + scale * d).collect(), scale < 1.)
}

/// Solve F(x)=0 using analytic diode Jacobians, bounded damping and a supplied
/// independently replaceable linear solver.
pub fn solve(plan: &CompiledCircuit, solver: &dyn LinearSolver, options: NewtonOptions) -> SimResult<(Vec<f64>, NewtonReport)> {
    solve_from(plan, solver, options, vec![0.; plan.unknown_count()])
}

/// Solve from an explicit finite MNA state. This is useful for continuation and
/// transient work, while the public operating-point path deliberately starts at zero.
pub fn solve_from(plan: &CompiledCircuit, solver: &dyn LinearSolver, options: NewtonOptions, initial: Vec<f64>) -> SimResult<(Vec<f64>, NewtonReport)> {
    options.validate()?;
    if initial.len() != plan.unknown_count() || initial.iter().any(|value| !value.is_finite()) {
        return Err(SimError::invalid("invalid Newton initial state"));
    }
    let mut values = initial;
    let baseline = norm_inf(&plan.rhs)?.max(1.);
    let mut damped_steps = 0;
    for iteration in 0..options.max_iterations {
        let system = linearize(plan, &values)?;
        let residual = norm_inf(&system.residual)?;
        if residual <= options.absolute_tolerance + options.relative_tolerance * baseline {
            return Ok((values, NewtonReport { iterations: iteration, residual_inf: residual, damped_steps }));
        }
        let rhs: Vec<f64> = system.residual.iter().map(|value| -value).collect();
        let delta = solver.solve(&system.jacobian, &rhs)?;
        if delta.len() != values.len() || delta.iter().any(|value| !value.is_finite()) {
            return Err(SimError::new(ErrorCode::NumericalFailure, "Newton linear solve returned invalid update"));
        }
        let mut accepted = None;
        for attempt in 0..12 {
            let (candidate, damped) = limited_candidate(plan, &values, &delta, 2f64.powi(-attempt));
            let candidate_residual = norm_inf(&linearize(plan, &candidate)?.residual)?;
            if candidate_residual < residual {
                accepted = Some((candidate, damped || attempt > 0));
                break;
            }
        }
        let Some((candidate, damped)) = accepted else {
            return Err(SimError::new(ErrorCode::NumericalFailure, "Newton line search failed to reduce residual"));
        };
        if damped { damped_steps += 1; }
        values = candidate;
    }
    Err(SimError::new(ErrorCode::NumericalFailure, "Newton iteration limit exceeded"))
}
