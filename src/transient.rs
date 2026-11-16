use crate::{
    compile::CompiledCircuit,
    dc::solve_csc_verified,
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
    plan.require_dynamic_native_models()?;
    // The legacy public Rust entry point retains its original admission. Only
    // configured BE has qualified behavioral startup and endpoint audits.
    if !plan.behaviors.is_empty() {
        return Err(SimError::new(crate::error::ErrorCode::Unsupported,
            "behavioral voltage laws require the qualified configured transient profile"));
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
        let accepted = if plan.has_nonlinear_devices() {
            crate::newton::solve_backward_euler_step(
                plan,
                solver,
                crate::newton::NewtonOptions::default(),
                &previous,
                h,
                &plan.rhs,
            )?
            .0
        } else {
            let system = crate::linear_dynamic::assemble(
                plan,
                crate::configured::Method::BackwardEuler,
                &previous,
                h,
                &plan.rhs,
                &plan.rhs,
            )?;
            let (values, backward_error) = solve_csc_verified(&system.matrix, &system.rhs, solver)?;
            tracing::debug!(
                step,
                time_s = next,
                step_s = h,
                backward_error,
                "linear_transient_step_completed"
            );
            values
        };
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
