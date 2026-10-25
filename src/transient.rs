use crate::{
    compile::CompiledCircuit,
    dc::solve_verified,
    error::{SimError, SimResult},
    linalg::LinearSolver,
    quantity::positive,
};
use serde::{Deserialize, Serialize};
#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(deny_unknown_fields)]
pub struct TransientOptions {
    pub stop_s: f64,
    pub step_s: f64,
    pub max_steps: usize,
}
/// Requested grid, fixed-step backward Euler. No adaptive-error-control claim.
#[derive(Debug, Clone, Serialize)]
pub struct Sample {
    pub time_s: f64,
    pub values: Vec<f64>,
}
pub fn transient(
    plan: &CompiledCircuit,
    solver: &dyn LinearSolver,
    options: &TransientOptions,
) -> SimResult<Vec<Sample>> {
    transient_controlled(
        plan,
        solver,
        options,
        &std::sync::atomic::AtomicBool::new(false),
    )
}
/// Cooperative cancellation is checked before every linear step.
pub fn transient_controlled(
    plan: &CompiledCircuit,
    solver: &dyn LinearSolver,
    options: &TransientOptions,
    cancel: &std::sync::atomic::AtomicBool,
) -> SimResult<Vec<Sample>> {
    if plan.has_nonlinear_devices() {
        return Err(crate::error::SimError::new(
            crate::error::ErrorCode::Unsupported,
            "nonlinear transient Newton stepping is not implemented",
        ));
    }
    positive(options.stop_s, "stop_s")?;
    positive(options.step_s, "step_s")?;
    let count = (options.stop_s / options.step_s).ceil().max(1.0);
    if options.max_steps == 0 || options.max_steps > 100_000 || count > options.max_steps as f64 {
        return Err(SimError::new(
            crate::error::ErrorCode::ResourceExhausted,
            "step budget exceeded",
        ));
    }
    if count * plan.conductance.n as f64 > 2_000_000.0 {
        return Err(SimError::new(
            crate::error::ErrorCode::ResourceExhausted,
            "retained waveform scalar budget exceeded",
        ));
    }
    let mut previous = vec![0.0; plan.conductance.n];
    let mut samples = Vec::with_capacity(count as usize);
    let mut time = 0.0;
    for step in 1..=count as usize {
        if cancel.load(std::sync::atomic::Ordering::Relaxed) {
            return Err(SimError::new(
                crate::error::ErrorCode::Cancelled,
                "cancelled before step",
            ));
        }
        let next = (step as f64 * options.step_s).min(options.stop_s);
        let h = next - time;
        if h <= 0.0 {
            break;
        }
        let mut a = plan.conductance.clone();
        let mut rhs = plan.rhs.clone();
        for (i, rhs_value) in rhs.iter_mut().enumerate() {
            for (j, previous_value) in previous.iter().enumerate() {
                let c = plan.charge.at(i, j) / h;
                a.add(i, j, c);
                *rhs_value += c * previous_value;
            }
        }
        let (accepted, _) = solve_verified(&a, &rhs, solver)?;
        // Commit only after the solve and physical residual checks succeed.
        previous = accepted;
        samples.push(Sample {
            time_s: next,
            values: previous.clone(),
        });
        time = next;
    }
    Ok(samples)
}
