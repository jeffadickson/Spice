//! Bounded direct sparse assembly for linear DAE time steps.
use crate::{
    compile::CompiledCircuit,
    configured::Method,
    error::{ErrorCode, SimError, SimResult},
    sparse::CscMatrix,
};

/// One immutable G+alpha*Q system and its excitation, before provider dispatch.
pub struct LinearStepSystem {
    /// Canonical union retains structural slots even when coefficients cancel.
    pub matrix: CscMatrix,
    /// Signed source and committed-history contributions in MNA row units.
    pub rhs: Vec<f64>,
}

/// Assemble BE or trapezoidal linear equations without scanning dense matrices.
/// Algebraic rows use current excitation only. Signed inductor flux rows retain
/// their compiled Q sign. Inputs are immutable; rejected trials cannot commit history.
pub fn assemble(
    plan: &CompiledCircuit,
    method: Method,
    previous: &[f64],
    step_s: f64,
    previous_rhs: &[f64],
    current_rhs: &[f64],
) -> SimResult<LinearStepSystem> {
    if plan.has_nonlinear_devices() {
        return Err(SimError::new(
            ErrorCode::Unsupported,
            "linear assembly cannot omit nonlinear devices",
        ));
    }
    let n = plan.unknown_count();
    if !step_s.is_finite()
        || step_s <= 0.
        || [previous, previous_rhs, current_rhs]
            .iter()
            .any(|v| v.len() != n || v.iter().any(|x| !x.is_finite()))
    {
        return Err(SimError::invalid("invalid linear dynamic inputs"));
    }
    let alpha = match method {
        Method::BackwardEuler => 1. / step_s,
        Method::Trapezoidal => 2. / step_s,
    };
    let matrix = plan
        .conductance_sparse()
        .combine_scaled(plan.charge_sparse(), 1., alpha)?;
    let mut rhs = current_rhs.to_vec();
    let mut dynamic = vec![false; n];
    for (row, column, value) in plan.charge_sparse().entries() {
        dynamic[row] |= value != 0.;
        rhs[row] += (alpha * value) * previous[column];
    }
    if method == Method::Trapezoidal {
        for (row, column, value) in plan.conductance_sparse().entries() {
            if dynamic[row] {
                rhs[row] -= value * previous[column];
            }
        }
        for row in 0..n {
            if dynamic[row] {
                rhs[row] += previous_rhs[row];
            }
        }
    }
    if rhs.iter().any(|value| !value.is_finite()) {
        return Err(SimError::invalid("linear dynamic history overflow"));
    }
    Ok(LinearStepSystem { matrix, rhs })
}
