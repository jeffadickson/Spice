//! Residual/Jacobian assembly and bounded damped Newton operating-point solves.

use crate::{
    compile::CompiledCircuit,
    error::{ErrorCode, SimError, SimResult},
    linalg::{LinearSolver, Matrix},
};

/// Residual F(x) and analytic Jacobian dF/dx at one immutable circuit trial.
#[derive(Debug, Clone)]
pub struct LinearizedSystem {
    pub residual: Vec<f64>,
    pub jacobian: Matrix,
}

fn voltage(values: &[f64], p: crate::ids::NodeId, n: crate::ids::NodeId) -> f64 {
    p.unknown().map(|index| values[index]).unwrap_or(0.)
        - n.unknown().map(|index| values[index]).unwrap_or(0.)
}

fn stamp_vector(values: &mut [f64], p: crate::ids::NodeId, n: crate::ids::NodeId, current: f64) {
    if let Some(index) = p.unknown() {
        values[index] += current;
    }
    if let Some(index) = n.unknown() {
        values[index] -= current;
    }
}

/// Assemble the MNA residual and analytic diode Jacobian at `values`.
pub fn linearize(plan: &CompiledCircuit, values: &[f64]) -> SimResult<LinearizedSystem> {
    linearize_with_rhs(plan, values, &plan.rhs)
}

/// Assemble against an explicit independent-source right-hand side.
///
/// Source continuation owns scaling this vector; the circuit topology, device
/// equations and Jacobian convention remain unchanged at every stage.
pub fn linearize_with_rhs(
    plan: &CompiledCircuit,
    values: &[f64],
    rhs: &[f64],
) -> SimResult<LinearizedSystem> {
    if values.len() != plan.unknown_count() || values.iter().any(|value| !value.is_finite()) {
        return Err(SimError::invalid("invalid Newton trial vector"));
    }
    if rhs.len() != values.len() || rhs.iter().any(|value| !value.is_finite()) {
        return Err(SimError::invalid("invalid Newton source right-hand side"));
    }
    let mut residual = vec![0.; values.len()];
    for (row, residual_value) in residual.iter_mut().enumerate() {
        *residual_value = (0..values.len())
            .map(|column| plan.conductance.at(row, column) * values[column])
            .sum::<f64>()
            - rhs[row];
    }
    let mut jacobian = plan.conductance.clone();
    for diode in &plan.diodes {
        let contribution = diode.model.linearize(voltage(values, diode.p, diode.n))?;
        stamp_vector(&mut residual, diode.p, diode.n, contribution.current_amps);
        crate::compile::stamp_pair(
            &mut jacobian,
            diode.p,
            diode.n,
            contribution.conductance_siemens,
        );
    }
    if residual.iter().any(|value| !value.is_finite()) {
        return Err(SimError::invalid("nonfinite Newton residual"));
    }
    Ok(LinearizedSystem { residual, jacobian })
}

/// Assemble a backward-Euler residual/Jacobian for one nonlinear transient step.
/// `previous` is committed only by the caller after this solve converges; this keeps
/// rejected trials from mutating capacitor/inductor history.
pub fn linearize_backward_euler(
    plan: &CompiledCircuit,
    values: &[f64],
    previous: &[f64],
    step_s: f64,
    rhs: &[f64],
) -> SimResult<LinearizedSystem> {
    if previous.len() != plan.unknown_count()
        || rhs.len() != plan.unknown_count()
        || !step_s.is_finite()
        || step_s <= 0.
    {
        return Err(SimError::invalid("invalid nonlinear transient step inputs"));
    }
    let mut system = linearize(plan, values)?;
    for (row, rhs_value) in rhs.iter().enumerate() {
        system.residual[row] += plan.rhs[row] - rhs_value;
        for column in 0..values.len() {
            let coefficient = plan.charge.at(row, column) / step_s;
            system.jacobian.add(row, column, coefficient);
            system.residual[row] += coefficient * (values[column] - previous[column]);
        }
    }
    if system.residual.iter().any(|value| !value.is_finite()) {
        return Err(SimError::invalid("nonfinite nonlinear transient residual"));
    }
    Ok(system)
}

/// Infinity norm used for scale-independent Newton convergence and line search.
pub fn norm_inf(values: &[f64]) -> SimResult<f64> {
    let norm = values
        .iter()
        .fold(0_f64, |worst, value| worst.max(value.abs()));
    if !norm.is_finite() {
        return Err(SimError::invalid("nonfinite Newton norm"));
    }
    Ok(norm)
}

/// Bounded Newton policy for the reference diode operating-point solver.
#[derive(Debug, Clone, Copy, serde::Serialize, serde::Deserialize)]
#[serde(deny_unknown_fields)]
pub struct NewtonOptions {
    pub max_iterations: usize,
    pub absolute_tolerance: f64,
    pub relative_tolerance: f64,
    /// Optional bounded source-stepping policy for difficult DC operating points.
    #[serde(default)]
    pub continuation: Option<ContinuationOptions>,
}

impl Default for NewtonOptions {
    fn default() -> Self {
        Self {
            max_iterations: 50,
            absolute_tolerance: 1e-12,
            relative_tolerance: 1e-9,
            continuation: None,
        }
    }
}

impl NewtonOptions {
    pub fn validate(self) -> SimResult<()> {
        if self.max_iterations == 0
            || self.max_iterations > 256
            || !self.absolute_tolerance.is_finite()
            || !self.relative_tolerance.is_finite()
            || self.absolute_tolerance <= 0.
            || self.relative_tolerance <= 0.
        {
            return Err(SimError::invalid("invalid Newton options"));
        }
        Ok(())
    }
}

/// Bounded source-stepping policy. Each accepted stage becomes the next Newton initial state.
#[derive(Debug, Clone, Copy, serde::Serialize, serde::Deserialize)]
#[serde(deny_unknown_fields)]
pub struct ContinuationOptions {
    /// Initial fraction of the full independent-source right-hand side.
    pub initial_step: f64,
    /// Smallest permitted source-fraction increment after failed stages.
    pub minimum_step: f64,
    /// Maximum accepted and rejected source-stage attempts combined.
    pub max_stages: usize,
}

impl Default for ContinuationOptions {
    fn default() -> Self {
        Self {
            initial_step: 0.125,
            minimum_step: 1e-4,
            max_stages: 64,
        }
    }
}

impl ContinuationOptions {
    pub fn validate(self) -> SimResult<()> {
        if !self.initial_step.is_finite()
            || !self.minimum_step.is_finite()
            || self.initial_step <= 0.
            || self.initial_step > 1.
            || self.minimum_step <= 0.
            || self.minimum_step > self.initial_step
            || self.max_stages == 0
            || self.max_stages > 1024
        {
            return Err(SimError::invalid("invalid continuation options"));
        }
        Ok(())
    }
}

/// Deterministic convergence evidence for an accepted operating point.
#[derive(Debug, Clone, serde::Serialize)]
pub struct NewtonReport {
    pub iterations: usize,
    pub residual_inf: f64,
    pub update_inf: f64,
    pub damped_steps: usize,
    pub linear_solves: usize,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub continuation: Option<ContinuationReport>,
}

/// One accepted source-stepping stage retained as reproducible convergence evidence.
#[derive(Debug, Clone, serde::Serialize)]
pub struct ContinuationStage {
    pub source_scale: f64,
    pub iterations: usize,
    pub residual_inf: f64,
    pub update_inf: f64,
    pub damped_steps: usize,
}

/// Aggregate source-stepping evidence; rejected attempts are counted but not presented as solves.
#[derive(Debug, Clone, serde::Serialize)]
pub struct ContinuationReport {
    pub accepted_stages: usize,
    pub rejected_stages: usize,
    pub stages: Vec<ContinuationStage>,
}

fn limited_candidate(
    plan: &CompiledCircuit,
    current: &[f64],
    delta: &[f64],
    alpha: f64,
) -> (Vec<f64>, bool) {
    let mut scale = alpha;
    for diode in &plan.diodes {
        let old = voltage(current, diode.p, diode.n);
        let change = voltage(delta, diode.p, diode.n);
        // A bounded 0.5 V terminal change avoids exponential overflow while
        // preserving a single scalar damping factor for the full MNA system.
        if change.abs() > 0.5 {
            scale = scale.min(0.5 / change.abs());
        }
        if old + scale * change > 17. {
            scale = scale.min((17. - old) / change);
        }
    }
    (
        current
            .iter()
            .zip(delta)
            .map(|(x, d)| x + scale * d)
            .collect(),
        scale < 1.,
    )
}

/// Solve F(x)=0 using analytic diode Jacobians, bounded damping and a supplied
/// independently replaceable linear solver.
pub fn solve(
    plan: &CompiledCircuit,
    solver: &dyn LinearSolver,
    options: NewtonOptions,
) -> SimResult<(Vec<f64>, NewtonReport)> {
    solve_from(plan, solver, options, vec![0.; plan.unknown_count()])
}

/// Solve from an explicit finite MNA state. This is useful for continuation and
/// transient work, while the public operating-point path deliberately starts at zero.
pub fn solve_from(
    plan: &CompiledCircuit,
    solver: &dyn LinearSolver,
    options: NewtonOptions,
    initial: Vec<f64>,
) -> SimResult<(Vec<f64>, NewtonReport)> {
    options.validate()?;
    if initial.len() != plan.unknown_count() || initial.iter().any(|value| !value.is_finite()) {
        return Err(SimError::invalid("invalid Newton initial state"));
    }
    if let Some(continuation) = options.continuation {
        continuation.validate()?;
        return solve_with_source_continuation(plan, solver, options, continuation, initial);
    }
    solve_from_rhs(plan, solver, options, initial, &plan.rhs)
}

/// Solves one fixed independent-source excitation with explicit residual/update evidence.
fn solve_from_rhs(
    plan: &CompiledCircuit,
    solver: &dyn LinearSolver,
    options: NewtonOptions,
    initial: Vec<f64>,
    rhs: &[f64],
) -> SimResult<(Vec<f64>, NewtonReport)> {
    let mut values = initial;
    let baseline = norm_inf(rhs)?.max(1.);
    let mut damped_steps = 0;
    let mut linear_solves = 0;
    let mut last_update = f64::INFINITY;
    for iteration in 0..options.max_iterations {
        let system = linearize_with_rhs(plan, &values, rhs)?;
        let residual = norm_inf(&system.residual)?;
        if residual <= options.absolute_tolerance + options.relative_tolerance * baseline {
            return Ok((
                values,
                NewtonReport {
                    iterations: iteration,
                    residual_inf: residual,
                    update_inf: if iteration == 0 { 0. } else { last_update },
                    damped_steps,
                    linear_solves,
                    continuation: None,
                },
            ));
        }
        let correction_rhs: Vec<f64> = system.residual.iter().map(|value| -value).collect();
        let delta = solver.solve(&system.jacobian, &correction_rhs)?;
        linear_solves += 1;
        if delta.len() != values.len() || delta.iter().any(|value| !value.is_finite()) {
            return Err(SimError::new(
                ErrorCode::NumericalFailure,
                "Newton linear solve returned invalid update",
            ));
        }
        let mut accepted = None;
        for attempt in 0..12 {
            let (candidate, damped) = limited_candidate(plan, &values, &delta, 2f64.powi(-attempt));
            let candidate_residual = norm_inf(&linearize_with_rhs(plan, &candidate, rhs)?.residual)?;
            if candidate_residual < residual {
                let update = candidate
                    .iter()
                    .zip(&values)
                    .map(|(candidate, previous)| candidate - previous)
                    .collect::<Vec<_>>();
                accepted = Some((candidate, damped || attempt > 0, norm_inf(&update)?));
                break;
            }
        }
        let Some((candidate, damped, update)) = accepted else {
            return Err(SimError::new(
                ErrorCode::NumericalFailure,
                "Newton line search failed to reduce residual",
            ));
        };
        if damped {
            damped_steps += 1;
        }
        last_update = update;
        values = candidate;
    }
    Err(SimError::new(
        ErrorCode::NumericalFailure,
        "Newton iteration limit exceeded",
    ))
}

/// Solves progressively scaled independent sources, shrinking failed increments.
fn solve_with_source_continuation(
    plan: &CompiledCircuit,
    solver: &dyn LinearSolver,
    options: NewtonOptions,
    continuation: ContinuationOptions,
    initial: Vec<f64>,
) -> SimResult<(Vec<f64>, NewtonReport)> {
    let mut source_scale = 0.0;
    let mut step = continuation.initial_step;
    let mut values = initial;
    let mut attempts = 0;
    let mut rejected_stages = 0;
    let mut stages = Vec::new();
    let mut final_report = None;
    while source_scale < 1.0 {
        if attempts >= continuation.max_stages {
            return Err(SimError::new(
                ErrorCode::NumericalFailure,
                format!("source continuation exhausted {} stage attempts at scale {source_scale}", continuation.max_stages),
            ));
        }
        attempts += 1;
        let target_scale = (source_scale + step).min(1.0);
        let scaled_rhs = plan.rhs.iter().map(|value| value * target_scale).collect::<Vec<_>>();
        match solve_from_rhs(plan, solver, options, values.clone(), &scaled_rhs) {
            Ok((candidate, report)) => {
                stages.push(ContinuationStage {
                    source_scale: target_scale,
                    iterations: report.iterations,
                    residual_inf: report.residual_inf,
                    update_inf: report.update_inf,
                    damped_steps: report.damped_steps,
                });
                source_scale = target_scale;
                values = candidate;
                final_report = Some(report);
                step = (step * 2.0).min(1.0 - source_scale);
            }
            Err(error) => {
                if step <= continuation.minimum_step {
                    return Err(SimError::new(
                        ErrorCode::NumericalFailure,
                        format!("source continuation failed at scale {target_scale} with minimum step {}: {}", continuation.minimum_step, error.message),
                    ));
                }
                rejected_stages += 1;
                step *= 0.5;
                if step < continuation.minimum_step {
                    return Err(SimError::new(
                        ErrorCode::NumericalFailure,
                        format!("source continuation cannot reduce below minimum step {} after failure at scale {target_scale}: {}", continuation.minimum_step, error.message),
                    ));
                }
            }
        }
    }
    let mut report = final_report.ok_or_else(|| SimError::invalid("source continuation produced no stages"))?;
    report.continuation = Some(ContinuationReport {
        accepted_stages: stages.len(),
        rejected_stages,
        stages,
    });
    Ok((values, report))
}

/// Solve a bounded backward-Euler nonlinear step from a finite committed state.
pub fn solve_backward_euler_step(
    plan: &CompiledCircuit,
    solver: &dyn LinearSolver,
    options: NewtonOptions,
    previous: &[f64],
    step_s: f64,
    rhs: &[f64],
) -> SimResult<(Vec<f64>, NewtonReport)> {
    options.validate()?;
    if options.continuation.is_some() {
        return Err(SimError::new(
            ErrorCode::Unsupported,
            "source continuation is currently limited to DC operating points",
        ));
    }
    let mut values = previous.to_vec();
    let baseline = norm_inf(rhs)?.max(1.);
    let mut damped_steps = 0;
    let mut linear_solves = 0;
    let mut last_update = f64::INFINITY;
    for iteration in 0..options.max_iterations {
        let system = linearize_backward_euler(plan, &values, previous, step_s, rhs)?;
        let residual = norm_inf(&system.residual)?;
        if residual <= options.absolute_tolerance + options.relative_tolerance * baseline {
            return Ok((
                values,
                NewtonReport {
                    iterations: iteration,
                    residual_inf: residual,
                    update_inf: if iteration == 0 { 0. } else { last_update },
                    damped_steps,
                    linear_solves,
                    continuation: None,
                },
            ));
        }
        let update = solver.solve(
            &system.jacobian,
            &system
                .residual
                .iter()
                .map(|value| -value)
                .collect::<Vec<_>>(),
        )?;
        linear_solves += 1;
        let mut accepted = None;
        for attempt in 0..12 {
            let (candidate, damped) =
                limited_candidate(plan, &values, &update, 2f64.powi(-attempt));
            if norm_inf(
                &linearize_backward_euler(plan, &candidate, previous, step_s, rhs)?.residual,
            )? < residual
            {
                let update_norm = norm_inf(
                    &candidate
                        .iter()
                        .zip(&values)
                        .map(|(candidate, previous)| candidate - previous)
                        .collect::<Vec<_>>(),
                )?;
                accepted = Some((candidate, damped || attempt > 0, update_norm));
                break;
            }
        }
        let Some((candidate, damped, update_norm)) = accepted else {
            return Err(SimError::new(
                ErrorCode::NumericalFailure,
                "nonlinear transient Newton line search failed",
            ));
        };
        if damped {
            damped_steps += 1;
        }
        last_update = update_norm;
        values = candidate;
    }
    Err(SimError::new(
        ErrorCode::NumericalFailure,
        "nonlinear transient Newton iteration limit exceeded",
    ))
}
