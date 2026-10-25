use crate::{
    error::{SimError, SimResult},
    quantity::positive,
    sources::SourceOverride,
};
use serde::{Deserialize, Serialize};
#[derive(Debug, Clone, Copy, PartialEq, Eq, Default, Serialize, Deserialize)]
#[serde(rename_all = "snake_case")]
pub enum Method {
    #[default]
    BackwardEuler,
    Trapezoidal,
}
/// Advanced linear path uses an operating point at t=0 unless a consistent full MNA state is supplied.
#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(deny_unknown_fields)]
pub struct TransientConfig {
    pub stop_s: f64,
    pub max_step_s: f64,
    pub max_steps: usize,
    #[serde(default)]
    pub method: Method,
    #[serde(default)]
    pub initial_state: Option<Vec<f64>>,
    #[serde(default)]
    pub sources: Vec<SourceOverride>,
}
impl Default for TransientConfig {
    fn default() -> Self {
        Self {
            stop_s: 0.005,
            max_step_s: 1e-5,
            max_steps: 10_000,
            method: Method::BackwardEuler,
            initial_state: None,
            sources: Vec::new(),
        }
    }
}
impl TransientConfig {
    pub fn validate(&self) -> SimResult<()> {
        positive(self.stop_s, "stop_s")?;
        positive(self.max_step_s, "max_step_s")?;
        if self.max_steps == 0 || self.max_steps > 100_000 {
            return Err(SimError::invalid(
                "configured step budget must be 1..100000",
            ));
        }
        Ok(())
    }
}

#[derive(Debug, Serialize)]
pub struct IntegrationReport {
    pub method: Method,
    pub initialization: &'static str,
    pub accepted_steps: usize,
}
pub struct ConfiguredResult {
    pub samples: Vec<crate::transient::Sample>,
    pub report: IntegrationReport,
}
/// Validate explicit algebraic constraints; differential rows may have nonzero derivatives.
fn initial(
    plan: &crate::compile::CompiledCircuit,
    b: &[f64],
    state: Option<&[f64]>,
    solver: &dyn crate::linalg::LinearSolver,
) -> SimResult<Vec<f64>> {
    let Some(x) = state else {
        return Ok(crate::dc::solve_verified(&plan.conductance, b, solver)?.0);
    };
    if x.len() != plan.unknown_count() || x.iter().any(|v| !v.is_finite()) {
        return Err(SimError::invalid("invalid initial state shape/values"));
    }
    for (i, &rhs) in b.iter().enumerate() {
        if (0..x.len()).all(|j| plan.charge.at(i, j) == 0.) {
            let residual = (0..x.len())
                .map(|j| plan.conductance.at(i, j) * x[j])
                .sum::<f64>()
                - rhs;
            if !residual.is_finite() || residual.abs() > 1e-10 * (1. + rhs.abs()) {
                return Err(SimError::invalid(format!(
                    "initial algebraic constraint failed at row {i}"
                )));
            }
        }
    }
    Ok(x.to_vec())
}
pub fn run(
    plan: &crate::compile::CompiledCircuit,
    solver: &dyn crate::linalg::LinearSolver,
    options: &TransientConfig,
    cancel: &std::sync::atomic::AtomicBool,
) -> SimResult<ConfiguredResult> {
    options.validate()?;
    let sources = crate::sources::BoundSources::new(plan, &options.sources)?;
    let mut old_rhs = sources.rhs(0.)?;
    let mut state = initial(plan, &old_rhs, options.initial_state.as_deref(), solver)?;
    let mut samples = vec![crate::transient::Sample {
        time_s: 0.,
        values: state.clone(),
    }];
    let mut time = 0.;
    let mut steps = 0;
    while time < options.stop_s {
        if cancel.load(std::sync::atomic::Ordering::Relaxed) {
            return Err(crate::error::SimError::new(
                crate::error::ErrorCode::Cancelled,
                "configured run cancelled",
            ));
        }
        if steps >= options.max_steps || (samples.len() + 1) * plan.unknown_count() > 2_000_000 {
            return Err(crate::error::SimError::new(
                crate::error::ErrorCode::ResourceExhausted,
                "configured run budget exhausted",
            ));
        }
        let mut next = (time + options.max_step_s).min(options.stop_s);
        if let Some(event) = sources.next_breakpoint(time, next) {
            next = event;
        }
        if next <= time {
            return Err(crate::error::SimError::new(
                crate::error::ErrorCode::ResourceExhausted,
                "timestep cannot advance",
            ));
        }
        let new_rhs = sources.rhs(next)?;
        let accepted = crate::integrator::advance(
            plan,
            solver,
            &state,
            next - time,
            crate::integrator::RhsPair {
                previous: &old_rhs,
                current: &new_rhs,
            },
            options.method,
        )?;
        state = accepted;
        old_rhs = new_rhs;
        time = next;
        steps += 1;
        samples.push(crate::transient::Sample {
            time_s: time,
            values: state.clone(),
        });
    }
    Ok(ConfiguredResult {
        samples,
        report: IntegrationReport {
            method: options.method,
            initialization: if options.initial_state.is_some() {
                "explicit_state"
            } else {
                "operating_point"
            },
            accepted_steps: steps,
        },
    })
}
