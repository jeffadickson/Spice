use crate::{
    adaptive::AdaptiveOptions,
    compile::CompiledCircuit,
    error::{ErrorCode, SimError, SimResult},
    linalg::LinearSolver,
    quantity::positive,
    sources::{BoundSources, SourceOverride},
    transient::Sample,
};
use serde::{Deserialize, Serialize};
#[derive(Debug, Clone, Copy, PartialEq, Eq, Default, Serialize, Deserialize)]
#[serde(rename_all = "snake_case")]
pub enum Method {
    #[default]
    BackwardEuler,
    Trapezoidal,
}
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
    /// Optional nonlinear time-zero bias policy. It does not change subsequent
    /// step Newton options or silently select another linear provider.
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub initial_newton: Option<crate::newton::NewtonOptions>,
    /// Requested upper bound on current convergence error in amperes. The
    /// runtime may solve more tightly to retain its independent audit floor.
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub current_absolute_tolerance: Option<f64>,
    #[serde(default)]
    pub sources: Vec<SourceOverride>,
    #[serde(default)]
    pub adaptive: Option<AdaptiveOptions>,
    /// Explicit accepted times, including zero and stop. Linear replay only;
    /// mutually exclusive with adaptive control. No interpolation or extra
    /// source-corner steps may silently change this caller-owned sequence.
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub accepted_times_s: Option<Vec<f64>>,
}
impl Default for TransientConfig {
    fn default() -> Self {
        Self {
            stop_s: 0.005,
            max_step_s: 1e-5,
            max_steps: 10_000,
            method: Method::BackwardEuler,
            initial_state: None,
            initial_newton: None,
            current_absolute_tolerance: None,
            sources: Vec::new(),
            adaptive: None,
            accepted_times_s: None,
        }
    }
}
impl TransientConfig {
    pub fn validate(&self) -> SimResult<()> {
        positive(self.stop_s, "stop_s")?;
        positive(self.max_step_s, "max_step_s")?;
        if let Some(tolerance) = self.current_absolute_tolerance {
            positive(tolerance, "current_absolute_tolerance")?;
        }
        if let Some(policy) = self.initial_newton {
            policy.validate()?;
            if self.initial_state.is_some() {
                return Err(SimError::invalid(
                    "initial_state and initial_newton are exclusive",
                ));
            }
            // A final physical bias is mandatory. Staged Gmin removes its
            // shunt; a fixed shunt would silently alter initial stored charge.
            if policy.gmin_siemens.is_some() {
                return Err(SimError::invalid("fixed initial Gmin is unsupported"));
            }
        }
        if self.max_steps == 0 || self.max_steps > 100_000 {
            return Err(SimError::invalid(
                "configured step budget must be 1..100000",
            ));
        }
        if let Some(options) = &self.adaptive {
            options.validate(self.max_step_s)?;
        }
        if let Some(times) = &self.accepted_times_s {
            let roundoff = 32. * f64::EPSILON * self.stop_s;
            if self.adaptive.is_some() || times.len() < 2
                || times.len() > self.max_steps + 1
                || times.first() != Some(&0.) || times.last() != Some(&self.stop_s)
                || times.iter().any(|t| !t.is_finite())
                || times.windows(2).any(|p| p[1] <= p[0] || p[1]-p[0] > self.max_step_s+roundoff)
            {
                return Err(SimError::invalid("invalid accepted time grid or conflicting adaptive control"));
            }
        }
        Ok(())
    }
}
#[derive(Debug, Serialize)]
pub struct IntegrationReport {
    pub method: Method,
    /// Explicit-grid physics replay is distinct from adaptive qualification.
    pub time_control: &'static str,
    /// Accepted nonlinear trapezoidal subintervals restarted with BE after
    /// startup or a source corner, including adaptive half steps. Endpoints permit independent
    /// audits and junction-current reconstruction to use the actual method.
    pub backward_euler_restarts: Vec<f64>,
    /// Actual step-Newton bound after applying the requested upper bound and
    /// the stricter default physical-consistency floor. Input remains unchanged.
    pub effective_current_absolute_tolerance: f64,
    /// Number of accepted fine-step midpoints retained for audits/observers.
    pub retained_midpoints: usize,
    /// Failed nonlinear trial intervals rejected before publishing any state.
    pub failed_nonlinear_trials: usize,
    pub initialization: &'static str,
    /// Actual nonlinear bias work, including continuation and final physical
    /// source scale/Gmin removal. Absent for explicit-state and linear starts.
    #[serde(skip_serializing_if = "Option::is_none")]
    pub initial_newton: Option<crate::newton::NewtonReport>,
    pub accepted_steps: usize,
    pub rejected_steps: usize,
    pub attempts: usize,
    pub max_accepted_error: f64,
    pub linear_solves: usize,
    pub prepared_factors: usize,
    pub prepared_reuses: usize,
}
pub struct ConfiguredResult {
    pub samples: Vec<Sample>,
    pub report: IntegrationReport,
    /// One entry per accepted output interval. Adaptive entries retain the
    /// actual first half-step state, never a reconstructed interpolation.
    pub midpoint_history: Vec<Option<Sample>>,
}
fn initial(
    plan: &CompiledCircuit,
    b: &[f64],
    state: Option<&[f64]>,
    solver: &dyn LinearSolver,
) -> SimResult<Vec<f64>> {
    let Some(x) = state else {
        return Ok(crate::dc::solve_csc_verified(plan.conductance_sparse(), b, solver)?.0);
    };
    if x.len() != plan.unknown_count() || x.iter().any(|v| !v.is_finite()) {
        return Err(SimError::invalid("invalid initial state shape/values"));
    }
    if b.len() != x.len() || b.iter().any(|value| !value.is_finite()) {
        return Err(SimError::invalid("invalid initial excitation"));
    }
    // Reuse the complete constitutive residual so newly added nonlinear devices
    // cannot be omitted from initial consistency checks. Evaluation also checks
    // model domains on dynamic rows before observers see the initial state.
    let system = crate::newton::linearize_with_rhs(plan, x, b)?;
    let mut algebraic = vec![true; x.len()];
    for (row, _, value) in crate::newton::charge_at_bias_csc(plan, x)?.entries() {
        if value != 0. {
            algebraic[row] = false;
        }
    }
    for (i, &rhs) in b.iter().enumerate() {
        if algebraic[i] {
            let residual = system.residual[i];
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
    plan: &CompiledCircuit,
    solver: &dyn LinearSolver,
    options: &TransientConfig,
    cancel: &std::sync::atomic::AtomicBool,
) -> SimResult<ConfiguredResult> {
    run_observed(plan, solver, options, cancel, |_| Ok(()))
}
/// Trusted Rust consumers can observe accepted samples, including before cancellation.
/// Callback failure aborts the run; it cannot turn partial data into a success result.
pub fn run_observed(
    plan: &CompiledCircuit,
    solver: &dyn LinearSolver,
    options: &TransientConfig,
    cancel: &std::sync::atomic::AtomicBool,
    mut observer: impl FnMut(&Sample) -> SimResult<()>,
) -> SimResult<ConfiguredResult> {
    plan.require_dynamic_native_models()?;
    options.validate()?;
    if options.accepted_times_s.as_ref().is_some_and(|times|
        times.len().checked_mul(plan.unknown_count()).is_none_or(|count| count>2_000_000)) {
        return Err(SimError::new(ErrorCode::ResourceExhausted,"accepted time grid exceeds recording budget"));
    }
    if options.accepted_times_s.is_some() && plan.has_nonlinear_devices() {
        return Err(SimError::new(ErrorCode::Unsupported,"accepted time grid currently requires a linear circuit"));
    }
    if options.method == Method::Trapezoidal { crate::newton::require_trapezoidal_models(plan)?; }
    if !plan.has_nonlinear_devices() && options.initial_newton.is_some() {
        return Err(SimError::new(
            ErrorCode::Unsupported,
            "initial_newton requires nonlinear transient initialization",
        ));
    }
    let sources = BoundSources::new(plan, &options.sources)?;
    if let Some(times) = &options.accepted_times_s {
        for pair in times.windows(2) {
            if sources.next_breakpoint(pair[0],pair[1]).is_some_and(|event| event < pair[1]) {
                return Err(SimError::invalid("accepted time grid omits a source breakpoint"));
            }
        }
    }
    let mut old_rhs = sources.rhs(0.)?;
    let with_current_tolerance = |mut policy: crate::newton::NewtonOptions| {
        if let Some(tolerance) = options.current_absolute_tolerance {
            policy.current_absolute_tolerance = policy.current_absolute_tolerance.min(tolerance);
        }
        // A loose deck ABSTOL must not turn an approximate bias into an output
        // that fails the independent 100-pA KCL gate. Tighter authored bounds
        // remain effective; the actual stricter policy is reported explicitly.
        policy.current_absolute_tolerance = policy.current_absolute_tolerance
            .min(crate::newton::NewtonOptions::default().current_absolute_tolerance);
        policy
    };
    let step_policy = with_current_tolerance(crate::newton::NewtonOptions::default());
    let (mut state, initialization_solves, initial_newton) =
        if plan.has_nonlinear_devices() && options.initial_state.is_none() {
            let policy = with_current_tolerance(options.initial_newton.unwrap_or_default());
            let (state, report) =
                if policy.continuation.is_some() || policy.gmin_continuation.is_some() {
                    // Existing continuation scales the compiled nominal sources.
                    // Never replace a distinct time-zero excitation by nominal DC
                    // merely to obtain a more convenient initialization problem.
                    if old_rhs != plan.rhs {
                        return Err(SimError::new(
                            ErrorCode::Unsupported,
                            "initial continuation requires nominal time-zero sources",
                        ));
                    }
                    crate::newton::solve(plan, solver, policy)?
                } else {
                    crate::newton::solve_with_rhs(
                        plan,
                        solver,
                        policy,
                        vec![0.; plan.unknown_count()],
                        &old_rhs,
                    )?
                };
            // A nonlinear operating point can require several provider calls;
            // counting it as one hides work before the first transient step.
            (state, report.linear_solves, Some(report))
        } else {
            (
                initial(plan, &old_rhs, options.initial_state.as_deref(), solver)?,
                usize::from(options.initial_state.is_none()),
                None,
            )
        };
    let mut samples = vec![Sample {
        time_s: 0.,
        values: state.clone(),
    }];
    observer(&samples[0])?;
    let mut stepper = crate::integrator::Stepper::new(plan, solver, options.method);
    let (mut time, mut proposed) = (0., options.max_step_s);
    let mut fixed_index = 1usize;
    let (mut steps, mut rejected, mut attempts) = (0, 0, 0);
    let mut max_error = 0_f64;
    let trial_provider = crate::newton::CorrectionCounter{inner:solver,calls:std::sync::atomic::AtomicUsize::new(0)};
    let mut failed_nonlinear_trials = 0usize;
    let mut restart = true;
    let mut backward_euler_restarts = Vec::new();
    let mut midpoint_history = Vec::new();
    let mut midpoint_count = 0usize;
    'intervals: while time < options.stop_s {
        if cancel.load(std::sync::atomic::Ordering::Relaxed) {
            return Err(SimError::new(
                ErrorCode::Cancelled,
                "configured run cancelled",
            ));
        }
        // Fixed/explicit intervals retain one new endpoint; step doubling also
        // retains one midpoint. Reserving two for every method rejected the
        // documented inclusive limit even when no midpoint would be recorded.
        let additional = if options.adaptive.is_some() {2} else {1};
        if steps >= options.max_steps || (samples.len() + midpoint_count + additional) * plan.unknown_count() > 2_000_000 {
            return Err(SimError::new(
                ErrorCode::ResourceExhausted,
                "configured run budget exhausted",
            ));
        }
        if options
            .adaptive
            .as_ref()
            .is_some_and(|a| attempts >= a.max_attempts)
        {
            return Err(SimError::new(
                ErrorCode::ResourceExhausted,
                "adaptive attempt budget exhausted",
            ));
        }
        // Absolute fixed-grid times avoid accumulating n rounded additions.
        // Snap only endpoint roundoff before source clipping, never skip a source corner.
        let candidate = if let Some(times) = &options.accepted_times_s {
            times[steps+1]
        } else if options.adaptive.is_none() {
            // A physical source corner can differ by an ULP from n*h. Do not
            // create a near-zero interval just to visit that reporting grid.
            let grid_roundoff=32.*f64::EPSILON*time.abs().max(options.max_step_s);
            let mut skipped=0;
            while fixed_index as f64 * options.max_step_s <= time+grid_roundoff {
                if skipped==2 { return Err(SimError::new(ErrorCode::ResourceExhausted,"fixed grid cannot advance past roundoff")); }
                fixed_index += 1;
                skipped += 1;
            }
            fixed_index as f64 * options.max_step_s
        } else {
            time + proposed
        };
        let mut next = candidate.min(options.stop_s);
        let endpoint_roundoff = 32. * f64::EPSILON * options.stop_s.max(time).max(proposed);
        if options.accepted_times_s.is_none() && options.stop_s - next <= endpoint_roundoff {
            next = options.stop_s;
        }
        // Prefer the authored corner over a roundoff-adjacent grid point, but
        // never merge two source corners or advance beyond the requested stop.
        let breakpoint = sources.next_breakpoint(time, if options.accepted_times_s.is_some() {next} else {(next+endpoint_roundoff).min(options.stop_s)});
        if let Some(event) = breakpoint {
            next = event;
        }
        if next <= time {
            return Err(SimError::new(
                ErrorCode::ResourceExhausted,
                "timestep cannot advance",
            ));
        }
        let h = next - time;
        let new_rhs = sources.rhs(next)?;
        let nonlinear_method = if restart && options.method == Method::Trapezoidal {
            Method::BackwardEuler
        } else { options.method };
        attempts += 1;
        // Numerical failure of a trial is recoverable only inside an explicit
        // adaptive budget. Shrink the whole interval, retaining its committed
        // state/RHS/restart flag. Invalid inputs and cancellation stay terminal.
        macro_rules! nonlinear_trial {
            ($old:expr,$step:expr,$rhs:expr,$before_rhs:expr) => {{
                match nonlinear_step(plan,&trial_provider,step_policy,$old,$step,$rhs,$before_rhs,nonlinear_method) {
                    Ok(value)=>value,
                    Err(error)=>{
                        if let Some(adaptive)=&options.adaptive {
                            if matches!(error.code,ErrorCode::NumericalFailure|ErrorCode::SingularMatrix) && h>adaptive.min_step_s {
                                rejected+=1;failed_nonlinear_trials+=1;
                                proposed=(h/2.).max(adaptive.min_step_s);
                                tracing::debug!(time_s=time,step_s=h,next_step_s=proposed,reason=%error,"adaptive_newton_trial_rejected");
                                continue 'intervals;
                            }
                        }
                        return Err(error);
                    }
                }
            }};
        }
        let (coarse, _coarse_solves) = if plan.has_nonlinear_devices() {
            let (values, report) = nonlinear_trial!(&state,h,&new_rhs,&old_rhs);
            (values, report.linear_solves)
        } else {
            (
                stepper.advance(
                    &state,
                    h,
                    crate::integrator::RhsPair {
                        previous: &old_rhs,
                        current: &new_rhs,
                    },
                )?,
                0,
            )
        };
        let mut accepted_midpoint = None;
        let accepted = if let Some(adaptive) = &options.adaptive {
            let mid = time + h / 2.;
            if mid <= time || mid >= next {
                return Err(SimError::new(
                    ErrorCode::NumericalFailure,
                    "adaptive midpoint cannot advance",
                ));
            }
            let mid_rhs = sources.rhs(mid)?;
            let (half, _half_solves) = if plan.has_nonlinear_devices() {
                let (values, report) = nonlinear_trial!(&state,h/2.,&mid_rhs,&old_rhs);
                (values, report.linear_solves)
            } else {
                (
                    stepper.advance(
                        &state,
                        h / 2.,
                        crate::integrator::RhsPair {
                            previous: &old_rhs,
                            current: &mid_rhs,
                        },
                    )?,
                    0,
                )
            };
            let (fine, _fine_solves) = if plan.has_nonlinear_devices() {
                let (values, report) = nonlinear_trial!(&half,next-mid,&new_rhs,&mid_rhs);
                (values, report.linear_solves)
            } else {
                (
                    stepper.advance(
                        &half,
                        next - mid,
                        crate::integrator::RhsPair {
                            previous: &mid_rhs,
                            current: &new_rhs,
                        },
                    )?,
                    0,
                )
            };
            let order = if options.method == Method::BackwardEuler ||
                plan.has_nonlinear_devices() && nonlinear_method == Method::BackwardEuler {
                1
            } else {
                2
            };
            let error = if restart && plan.has_nonlinear_devices() && options.method==Method::Trapezoidal {
                adaptive.restart_error(plan,&coarse,&fine,order)?
            } else {adaptive.error(plan, &coarse, &fine, order)?};
            if error > 1. {
                rejected += 1;
                if h <= adaptive.min_step_s * (1. + 1e-12) {
                    return Err(SimError::new(
                        ErrorCode::NumericalFailure,
                        "local error exceeds tolerance at minimum step",
                    ));
                }
                proposed = (h / 2.).max(adaptive.min_step_s);
                continue;
            }
            max_error = max_error.max(error);
            let factor = if error == 0. {
                2.
            } else {
                (0.9 * error.powf(-1. / (order + 1) as f64)).clamp(0.5, 2.)
            };
            proposed = (h * factor).clamp(adaptive.min_step_s, options.max_step_s);
            accepted_midpoint = Some(Sample{time_s:mid,values:half});
            fine
        } else {
            coarse
        };
        // Only accepted states, RHS values and times become visible to future steps.
        if plan.has_nonlinear_devices() && nonlinear_method == Method::BackwardEuler
            && options.method == Method::Trapezoidal {
            if let Some(mid) = &accepted_midpoint { backward_euler_restarts.push(mid.time_s); }
            backward_euler_restarts.push(next);
        }
        if accepted_midpoint.is_some() { midpoint_count += 1; }
        midpoint_history.push(accepted_midpoint);
        restart = breakpoint == Some(next);
        state = accepted;
        old_rhs = new_rhs;
        time = next;
        steps += 1;
        samples.push(Sample {
            time_s: time,
            values: state.clone(),
        });
        observer(samples.last().expect("just appended accepted sample"))?;
    }
    Ok(ConfiguredResult {
        samples,
        midpoint_history,
        report: IntegrationReport {
            method: options.method,
            time_control: if options.accepted_times_s.is_some() {"accepted_time_grid"} else if options.adaptive.is_some() {"adaptive"} else {"fixed_step"},
            backward_euler_restarts,
            effective_current_absolute_tolerance: step_policy.current_absolute_tolerance,
            retained_midpoints: midpoint_count,
            failed_nonlinear_trials,
            initialization: if options.initial_state.is_some() {
                "explicit_state"
            } else {
                "operating_point"
            },
            initial_newton,
            accepted_steps: steps,
            rejected_steps: rejected,
            attempts,
            max_accepted_error: max_error,
            linear_solves: stepper.solve_calls + trial_provider.calls.load(std::sync::atomic::Ordering::Relaxed) + initialization_solves,
            prepared_factors: stepper.prepared_factors,
            prepared_reuses: stepper.prepared_reuses,
        },
    })
}

// One method dispatch for coarse and both fine trials keeps rejected adaptive
// work private and uses the matching previous excitation at every endpoint.
fn nonlinear_step(
    plan: &CompiledCircuit, solver: &dyn LinearSolver,
    policy: crate::newton::NewtonOptions, previous: &[f64], h: f64,
    rhs: &[f64], old_rhs: &[f64], method: Method,
) -> SimResult<(Vec<f64>, crate::newton::NewtonReport)> {
    match method {
        Method::BackwardEuler => crate::newton::solve_backward_euler_step(plan,solver,policy,previous,h,rhs),
        Method::Trapezoidal => crate::newton::solve_trapezoidal_step(plan,solver,policy,previous,h,rhs,old_rhs),
    }
}
