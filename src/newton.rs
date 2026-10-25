//! Residual/Jacobian assembly and bounded damped Newton operating-point solves.

use crate::{compile::CompiledCircuit, error::{SimError, SimResult}, linalg::Matrix};

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
