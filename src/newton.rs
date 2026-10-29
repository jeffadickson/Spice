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

/// Assemble incremental conductance directly in CSC at a finite bias vector.
/// This omits source/residual work and preserves diode terminal polarity.
/// DC bias convergence remains the caller's responsibility.
pub fn conductance_at_bias_csc(
    plan: &CompiledCircuit,
    values: &[f64],
) -> SimResult<crate::sparse::CscMatrix> {
    if values.len() != plan.unknown_count() || values.iter().any(|value| !value.is_finite()) {
        return Err(SimError::invalid("invalid incremental bias vector"));
    }
    let mut stamps = crate::sparse::SparseAccumulator::new(plan.unknown_count())?;
    for ((row, column), value) in plan
        .conductance_sparse()
        .pattern()
        .slots()
        .zip(plan.conductance_sparse().values())
    {
        stamps.add(row, column, *value);
    }
    for diode in &plan.diodes {
        let contribution = diode.model.linearize(voltage(values, diode.p, diode.n))?;
        crate::compile::stamp_pair(
            &mut stamps,
            diode.p,
            diode.n,
            contribution.conductance_siemens,
        );
    }
    stamps.to_csc()
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
    linearize_with_rhs_gmin(plan, values, rhs, None)
}

/// Adds only explicitly requested DC Gmin shunts; it never changes a nominal solve silently.
fn linearize_with_rhs_gmin(
    plan: &CompiledCircuit,
    values: &[f64],
    rhs: &[f64],
    gmin_siemens: Option<f64>,
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
    if let Some(gmin) = gmin_siemens {
        for node in 0..plan.circuit.nodes.len() - 1 {
            residual[node] += gmin * values[node];
            jacobian.add(node, node, gmin);
        }
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
        || previous.iter().any(|value| !value.is_finite())
        || rhs.iter().any(|value| !value.is_finite())
        || !step_s.is_finite()
        || step_s <= 0.
    {
        return Err(SimError::invalid("invalid nonlinear transient step inputs"));
    }
    // Accepted charge history must be representable independently of the
    // chosen step size. Scaling an overflowing Q by a large h must not hide it.
    for diode in &plan.diodes {
        if let Some(farads) = diode.model.capacitance() {
            crate::charge::constant_capacitance(farads, voltage(previous, diode.p, diode.n))?;
        }
    }
    // Assemble the requested excitation directly. Cancelling the nominal RHS
    // afterwards can overflow even when the requested residual is finite.
    let mut system = linearize_with_rhs(plan, values, rhs)?;
    for row in 0..values.len() {
        for column in 0..values.len() {
            let coefficient = plan.charge.at(row, column) / step_s;
            if coefficient == 0. {
                continue;
            }
            system.jacobian.add(row, column, coefficient);
            let difference = values[column] - previous[column];
            // If subtraction overflows, separately scaled finite terms may
            // still represent a finite displacement current.
            let displacement = if difference.is_finite() {
                coefficient * difference
            } else {
                coefficient * values[column] - coefficient * previous[column]
            };
            system.residual[row] += displacement;
        }
    }
    if system.residual.iter().any(|value| !value.is_finite()) {
        return Err(SimError::invalid("nonfinite nonlinear transient residual"));
    }
    Ok(system)
}

/// Infinity norm used for scale-independent Newton convergence and line search.
pub fn norm_inf(values: &[f64]) -> SimResult<f64> {
    // f64::max deliberately returns its other operand for NaN. Reject every
    // nonfinite component before reducing, or NaN could masquerade as zero.
    if values.iter().any(|value| !value.is_finite()) {
        return Err(SimError::invalid("nonfinite Newton norm"));
    }
    let norm = values
        .iter()
        .fold(0_f64, |worst, value| worst.max(value.abs()));
    if !norm.is_finite() {
        return Err(SimError::invalid("nonfinite Newton norm"));
    }
    Ok(norm)
}

/// Provider success is insufficient: every MNA correction must match the plan
/// and remain finite before any diode terminal indexing or history trial.
fn validate_correction(plan: &CompiledCircuit, correction: &[f64]) -> SimResult<()> {
    if correction.len() != plan.unknown_count() || correction.iter().any(|value| !value.is_finite())
    {
        return Err(SimError::new(
            ErrorCode::NumericalFailure,
            "Newton linear solve returned invalid update",
        ));
    }
    Ok(())
}

/// Private accounting adapter for the real correction path used by Newton.
/// The counter belongs to one invocation, including failed continuation stages.
struct CorrectionCounter<'a> {
    inner: &'a dyn LinearSolver,
    calls: std::sync::atomic::AtomicUsize,
}
impl LinearSolver for CorrectionCounter<'_> {
    fn name(&self) -> &'static str {
        self.inner.name()
    }
    fn solve(&self, matrix: &Matrix, rhs: &[f64]) -> SimResult<Vec<f64>> {
        self.calls
            .fetch_add(1, std::sync::atomic::Ordering::Relaxed);
        self.inner.solve(matrix, rhs)
    }
}

/// Dimension-aware norms: MNA node rows are current equations; branch rows are voltage equations.
/// Overflow in a finite tolerance calculation must never become a zero ratio.
fn scaled_component(value: f64, reference: f64, absolute: f64, relative: f64) -> SimResult<f64> {
    let scale = absolute + relative * reference.abs().max(absolute);
    let ratio = value.abs() / scale;
    if !value.is_finite()
        || !reference.is_finite()
        || !scale.is_finite()
        || scale <= 0.
        || !ratio.is_finite()
    {
        return Err(SimError::invalid(
            "nonfinite Newton tolerance scale or ratio",
        ));
    }
    Ok(ratio)
}

fn scaled_residual_norm(
    plan: &CompiledCircuit,
    residual: &[f64],
    rhs: &[f64],
    options: NewtonOptions,
) -> SimResult<f64> {
    if residual.len() != rhs.len() || residual.len() != plan.unknown_count() {
        return Err(SimError::invalid("invalid scaled residual"));
    }
    let node_rows = plan.circuit.nodes.len() - 1;
    let mut worst = 0_f64;
    for (index, value) in residual.iter().enumerate() {
        let absolute = if index < node_rows {
            options.current_absolute_tolerance
        } else {
            options.voltage_absolute_tolerance
        };
        worst = worst.max(scaled_component(
            *value,
            rhs[index],
            absolute,
            options.relative_tolerance,
        )?);
    }
    if worst.is_finite() {
        Ok(worst)
    } else {
        Err(SimError::invalid("nonfinite scaled residual"))
    }
}

fn scaled_update_norm(
    plan: &CompiledCircuit,
    update: &[f64],
    values: &[f64],
    options: NewtonOptions,
) -> SimResult<f64> {
    if update.len() != values.len() || update.len() != plan.unknown_count() {
        return Err(SimError::invalid("invalid scaled update"));
    }
    let node_values = plan.circuit.nodes.len() - 1;
    let mut worst = 0_f64;
    for (index, value) in update.iter().enumerate() {
        let absolute = if index < node_values {
            options.voltage_absolute_tolerance
        } else {
            options.current_absolute_tolerance
        };
        worst = worst.max(scaled_component(
            *value,
            values[index],
            absolute,
            options.relative_tolerance,
        )?);
    }
    if worst.is_finite() {
        Ok(worst)
    } else {
        Err(SimError::invalid("nonfinite scaled update"))
    }
}

/// Bounded Newton policy for the reference diode operating-point solver.
#[derive(Debug, Clone, Copy, serde::Serialize, serde::Deserialize)]
#[serde(deny_unknown_fields)]
pub struct NewtonOptions {
    pub max_iterations: usize,
    /// Retained and validated for legacy wire compatibility. Convergence uses
    /// the explicit voltage/current tolerances below, not this unitless field.
    pub absolute_tolerance: f64,
    pub relative_tolerance: f64,
    #[serde(default = "default_voltage_tolerance")]
    pub voltage_absolute_tolerance: f64,
    #[serde(default = "default_current_tolerance")]
    pub current_absolute_tolerance: f64,
    /// Optional bounded source-stepping policy for difficult DC operating points.
    #[serde(default)]
    pub continuation: Option<ContinuationOptions>,
    /// Optional explicit DC shunt conductance from each non-reference node to ground.
    #[serde(default)]
    pub gmin_siemens: Option<f64>,
    /// Optional staged Gmin warm-start policy; the final stage is always unregularized.
    #[serde(default)]
    pub gmin_continuation: Option<GminContinuationOptions>,
}

impl Default for NewtonOptions {
    fn default() -> Self {
        Self {
            max_iterations: 50,
            absolute_tolerance: 1e-12,
            relative_tolerance: 1e-9,
            voltage_absolute_tolerance: 1e-9,
            current_absolute_tolerance: 1e-12,
            continuation: None,
            gmin_siemens: None,
            gmin_continuation: None,
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
            || !self.voltage_absolute_tolerance.is_finite()
            || self.voltage_absolute_tolerance <= 0.
            || !self.current_absolute_tolerance.is_finite()
            || self.current_absolute_tolerance <= 0.
        {
            return Err(SimError::invalid("invalid Newton options"));
        }
        if self
            .gmin_siemens
            .is_some_and(|value| !value.is_finite() || value <= 0. || value > 1.)
        {
            return Err(SimError::invalid("invalid gmin_siemens"));
        }
        if self.gmin_siemens.is_some() && self.gmin_continuation.is_some() {
            return Err(SimError::invalid(
                "gmin_siemens and gmin_continuation are exclusive",
            ));
        }
        if self.continuation.is_some() && self.gmin_continuation.is_some() {
            return Err(SimError::invalid(
                "source continuation and gmin_continuation are exclusive",
            ));
        }
        if let Some(continuation) = self.gmin_continuation {
            continuation.validate()?;
        }
        if let Some(continuation) = self.continuation {
            continuation.validate()?;
        }
        Ok(())
    }
}
fn default_voltage_tolerance() -> f64 {
    1e-9
}
fn default_current_tolerance() -> f64 {
    1e-12
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

/// Bounded DC Gmin stepping. Accepted regularized stages warm-start one final,
/// physically unregularized Newton solve; they are never returned as the result.
#[derive(Debug, Clone, Copy, serde::Serialize, serde::Deserialize)]
#[serde(deny_unknown_fields)]
pub struct GminContinuationOptions {
    pub initial_siemens: f64,
    pub minimum_siemens: f64,
    pub reduction_factor: f64,
    pub max_stages: usize,
}
impl Default for GminContinuationOptions {
    fn default() -> Self {
        Self {
            initial_siemens: 1e-3,
            minimum_siemens: 1e-12,
            reduction_factor: 0.1,
            max_stages: 16,
        }
    }
}
impl GminContinuationOptions {
    pub fn validate(self) -> SimResult<()> {
        if !self.initial_siemens.is_finite()
            || !self.minimum_siemens.is_finite()
            || !self.reduction_factor.is_finite()
            || self.initial_siemens <= 0.
            || self.minimum_siemens <= 0.
            || self.minimum_siemens > self.initial_siemens
            || self.reduction_factor <= 0.
            || self.reduction_factor >= 1.
            || self.max_stages == 0
            || self.max_stages > 128
        {
            return Err(SimError::invalid("invalid Gmin continuation options"));
        }
        Ok(())
    }
}

/// Deterministic convergence evidence for an accepted operating point.
/// Iteration/damping fields describe the final stage; linear_solves counts all
/// real correction calls, including rejected recovery stages and final Gmin removal.
#[derive(Debug, Clone, serde::Serialize)]
pub struct NewtonReport {
    pub iterations: usize,
    pub residual_inf: f64,
    pub update_inf: f64,
    pub damped_steps: usize,
    pub linear_solves: usize,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub gmin_siemens: Option<f64>,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub continuation: Option<ContinuationReport>,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub gmin_continuation: Option<GminContinuationReport>,
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
#[derive(Debug, Clone, serde::Serialize)]
pub struct GminContinuationStage {
    pub gmin_siemens: f64,
    pub iterations: usize,
    pub residual_inf: f64,
}
#[derive(Debug, Clone, serde::Serialize)]
pub struct GminContinuationReport {
    pub stages: Vec<GminContinuationStage>,
    pub final_unregularized: bool,
}

fn limited_candidate(
    plan: &CompiledCircuit,
    current: &[f64],
    delta: &[f64],
    alpha: f64,
) -> (Vec<f64>, bool) {
    let mut limit = 1_f64;
    for diode in &plan.diodes {
        let old = voltage(current, diode.p, diode.n);
        let change = voltage(delta, diode.p, diode.n);
        // Thermal-voltage scaling keeps the same exponent increment across
        // valid diode parameter ranges. The full MNA vector shares one scale.
        let maximum_change = (2. * diode.model.thermal_volts()).min(0.5);
        if change.abs() > maximum_change {
            limit = limit.min(maximum_change / change.abs());
        }
        if change > 0. {
            let ceiling = (700. * diode.model.thermal_volts()).min(f64::MAX);
            limit = limit.min(((ceiling - old).max(0.) / change).min(1.));
        }
    }
    // Backtracking scales the already-limited direction. min(alpha, limit)
    // repeats an identical candidate until alpha becomes smaller than limit.
    let scale = alpha * limit;
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
    let counted = CorrectionCounter {
        inner: solver,
        calls: std::sync::atomic::AtomicUsize::new(0),
    };
    let (values, mut report) = if let Some(continuation) = options.continuation {
        continuation.validate()?;
        solve_with_source_continuation(plan, &counted, options, continuation, initial)
    } else if let Some(continuation) = options.gmin_continuation {
        solve_with_gmin_continuation(plan, &counted, options, continuation, initial)
    } else {
        solve_from_rhs(plan, &counted, options, initial, &plan.rhs)
    }?;
    report.linear_solves = counted.calls.load(std::sync::atomic::Ordering::Relaxed);
    tracing::debug!(
        final_iterations = report.iterations,
        linear_solves_total = report.linear_solves,
        "newton_completed"
    );
    Ok((values, report))
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
    let mut damped_steps = 0;
    let mut last_update = f64::INFINITY;
    for iteration in 0..=options.max_iterations {
        let system = linearize_with_rhs_gmin(plan, &values, rhs, options.gmin_siemens)?;
        let residual = norm_inf(&system.residual)?;
        tracing::trace!(iteration, residual_inf = residual, "newton_iteration");
        let scaled_residual = scaled_residual_norm(plan, &system.residual, rhs, options)?;
        if scaled_residual <= 1. && (iteration == 0 || last_update <= 1.) {
            return Ok((
                values,
                NewtonReport {
                    iterations: iteration,
                    residual_inf: residual,
                    update_inf: if iteration == 0 { 0. } else { last_update },
                    damped_steps,
                    linear_solves: iteration,
                    gmin_siemens: options.gmin_siemens,
                    continuation: None,
                    gmin_continuation: None,
                },
            ));
        }
        // Check the last permitted correction before declaring exhaustion,
        // without spending an additional provider call.
        if iteration == options.max_iterations {
            break;
        }
        let correction_rhs: Vec<f64> = system.residual.iter().map(|value| -value).collect();
        let delta = solver.solve(&system.jacobian, &correction_rhs)?;
        validate_correction(plan, &delta)?;
        let mut accepted = None;
        for attempt in 0..12 {
            let (candidate, damped) = limited_candidate(plan, &values, &delta, 2f64.powi(-attempt));
            let Ok(candidate_system) =
                linearize_with_rhs_gmin(plan, &candidate, rhs, options.gmin_siemens)
            else {
                // A trial can overflow even with finite provider components.
                // Smaller steps may remain inside the already-validated model.
                continue;
            };
            let candidate_residual =
                scaled_residual_norm(plan, &candidate_system.residual, rhs, options)?;
            if candidate_residual < scaled_residual || candidate_residual <= 1. {
                let update = candidate
                    .iter()
                    .zip(&values)
                    .map(|(candidate, previous)| candidate - previous)
                    .collect::<Vec<_>>();
                accepted = Some((
                    candidate,
                    damped || attempt > 0,
                    scaled_update_norm(plan, &update, &values, options)?,
                ));
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

fn solve_with_gmin_continuation(
    plan: &CompiledCircuit,
    solver: &dyn LinearSolver,
    options: NewtonOptions,
    continuation: GminContinuationOptions,
    initial: Vec<f64>,
) -> SimResult<(Vec<f64>, NewtonReport)> {
    let mut values = initial;
    let mut gmin = continuation.initial_siemens;
    let mut stages = Vec::new();
    for _ in 0..continuation.max_stages {
        let mut stage_options = options;
        stage_options.gmin_continuation = None;
        stage_options.gmin_siemens = Some(gmin);
        let (candidate, report) = solve_from_rhs(plan, solver, stage_options, values, &plan.rhs)?;
        tracing::debug!(
            gmin_siemens = gmin,
            iterations = report.iterations,
            "gmin_stage_accepted"
        );
        stages.push(GminContinuationStage {
            gmin_siemens: gmin,
            iterations: report.iterations,
            residual_inf: report.residual_inf,
        });
        values = candidate;
        if gmin <= continuation.minimum_siemens {
            break;
        }
        let next_gmin = (gmin * continuation.reduction_factor).max(continuation.minimum_siemens);
        if next_gmin >= gmin {
            return Err(SimError::new(
                ErrorCode::NumericalFailure,
                "Gmin continuation cannot make representable conductance progress",
            ));
        }
        gmin = next_gmin;
    }
    if stages
        .last()
        .is_some_and(|stage| stage.gmin_siemens > continuation.minimum_siemens)
    {
        return Err(SimError::new(
            ErrorCode::NumericalFailure,
            "Gmin continuation stage budget exhausted",
        ));
    }
    let mut final_options = options;
    final_options.gmin_continuation = None;
    final_options.gmin_siemens = None;
    tracing::debug!("gmin_physical_final_solve");
    let (values, mut report) = solve_from_rhs(plan, solver, final_options, values, &plan.rhs)?;
    report.gmin_continuation = Some(GminContinuationReport {
        stages,
        final_unregularized: true,
    });
    Ok((values, report))
}

/// Solves one explicitly supplied DC excitation without enabling continuation.
/// Configured transient initialization uses this to honor source values at t=0.
pub fn solve_with_rhs(
    plan: &CompiledCircuit,
    solver: &dyn LinearSolver,
    options: NewtonOptions,
    initial: Vec<f64>,
    rhs: &[f64],
) -> SimResult<(Vec<f64>, NewtonReport)> {
    options.validate()?;
    if options.continuation.is_some() || options.gmin_continuation.is_some() {
        return Err(SimError::new(
            ErrorCode::Unsupported,
            "continuation is only valid for the circuit's nominal DC source vector",
        ));
    }
    if initial.len() != plan.unknown_count() || initial.iter().any(|value| !value.is_finite()) {
        return Err(SimError::invalid("invalid Newton initial state"));
    }
    solve_from_rhs(plan, solver, options, initial, rhs)
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
                format!(
                    "source continuation exhausted {} stage attempts at scale {source_scale}",
                    continuation.max_stages
                ),
            ));
        }
        attempts += 1;
        let target_scale = (source_scale + step).min(1.0);
        if target_scale <= source_scale {
            return Err(SimError::new(
                ErrorCode::NumericalFailure,
                "source continuation cannot make representable excitation progress",
            ));
        }
        let scaled_rhs = plan
            .rhs
            .iter()
            .map(|value| value * target_scale)
            .collect::<Vec<_>>();
        match solve_from_rhs(plan, solver, options, values.clone(), &scaled_rhs) {
            Ok((candidate, report)) => {
                tracing::debug!(
                    source_scale = target_scale,
                    iterations = report.iterations,
                    "source_stage_accepted"
                );
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
                // A smaller source increment can repair conditioning or a
                // nonlinear failure, not cancellation, transport or contracts.
                if !matches!(
                    error.code,
                    ErrorCode::NumericalFailure | ErrorCode::SingularMatrix
                ) {
                    tracing::debug!(source_scale = target_scale, error_code = ?error.code, "source_stage_terminal_error");
                    return Err(error);
                }
                if step <= continuation.minimum_step {
                    return Err(SimError::new(
                        ErrorCode::NumericalFailure,
                        format!(
                            "source continuation failed at scale {target_scale} with minimum step {}: {}",
                            continuation.minimum_step, error.message
                        ),
                    ));
                }
                tracing::debug!(source_scale = target_scale, error_code = ?error.code, "source_stage_rejected");
                rejected_stages += 1;
                step *= 0.5;
                if step < continuation.minimum_step {
                    return Err(SimError::new(
                        ErrorCode::NumericalFailure,
                        format!(
                            "source continuation cannot reduce below minimum step {} after failure at scale {target_scale}: {}",
                            continuation.minimum_step, error.message
                        ),
                    ));
                }
            }
        }
    }
    let mut report =
        final_report.ok_or_else(|| SimError::invalid("source continuation produced no stages"))?;
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
    if options.continuation.is_some()
        || options.gmin_continuation.is_some()
        || options.gmin_siemens.is_some()
    {
        return Err(SimError::new(
            ErrorCode::Unsupported,
            "continuation and Gmin regularization are limited to DC operating points",
        ));
    }
    let mut values = previous.to_vec();
    let mut damped_steps = 0;
    let mut last_update = f64::INFINITY;
    for iteration in 0..=options.max_iterations {
        let system = linearize_backward_euler(plan, &values, previous, step_s, rhs)?;
        let residual = norm_inf(&system.residual)?;
        tracing::trace!(iteration, residual_inf = residual, "newton_iteration");
        let scaled_residual = scaled_residual_norm(plan, &system.residual, rhs, options)?;
        if scaled_residual <= 1. && (iteration == 0 || last_update <= 1.) {
            return Ok((
                values,
                NewtonReport {
                    iterations: iteration,
                    residual_inf: residual,
                    update_inf: if iteration == 0 { 0. } else { last_update },
                    damped_steps,
                    linear_solves: iteration,
                    gmin_siemens: None,
                    continuation: None,
                    gmin_continuation: None,
                },
            ));
        }
        if iteration == options.max_iterations {
            break;
        }
        let update = solver.solve(
            &system.jacobian,
            &system
                .residual
                .iter()
                .map(|value| -value)
                .collect::<Vec<_>>(),
        )?;
        validate_correction(plan, &update)?;
        let mut accepted = None;
        for attempt in 0..12 {
            let (candidate, damped) =
                limited_candidate(plan, &values, &update, 2f64.powi(-attempt));
            let Ok(candidate_system) =
                linearize_backward_euler(plan, &candidate, previous, step_s, rhs)
            else {
                continue;
            };
            let candidate_residual =
                scaled_residual_norm(plan, &candidate_system.residual, rhs, options)?;
            if candidate_residual < scaled_residual || candidate_residual <= 1. {
                let update_values = candidate
                    .iter()
                    .zip(&values)
                    .map(|(candidate, previous)| candidate - previous)
                    .collect::<Vec<_>>();
                let update_norm = scaled_update_norm(plan, &update_values, &values, options)?;
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

#[cfg(test)]
mod trial_contracts {
    use super::limited_candidate;
    use crate::{
        circuit::{Circuit, Element},
        compile::compile,
        ids::NodeId,
    };

    #[test]
    fn trial_ceiling_and_damping_evidence_have_exact_boundary_semantics() {
        let vt = 0.025;
        let plan = compile(&Circuit {
            nodes: vec!["0".into(), "x".into()],
            elements: vec![Element::Diode {
                id: "D".into(),
                p: NodeId(1),
                n: NodeId(0),
                saturation_amps: 1e-300,
                thermal_volts: vt,
            }],
        })
        .unwrap();
        // Close to the accepted exponent ceiling, the domain bound is tighter
        // than the two-Vt increment. Backtracking must contract that bound.
        let old = 699.5 * vt;
        let ceiling = 700. * vt;
        let (full, damped) = limited_candidate(&plan, &[old], &[2. * vt], 1.);
        assert!((full[0] - ceiling).abs() < 1e-14);
        assert!(damped);
        let (half, damped) = limited_candidate(&plan, &[old], &[2. * vt], 0.5);
        assert!((half[0] - (old + (ceiling - old) * 0.5)).abs() < 1e-14);
        assert!(damped);
        // A safe negative correction is not limited. Its evidence flag must
        // distinguish a full step from an explicitly backtracked step.
        let (reverse, damped) = limited_candidate(&plan, &[old], &[-vt], 1.);
        assert_eq!(reverse[0], old - vt);
        assert!(!damped);
        assert!(limited_candidate(&plan, &[old], &[-vt], 0.5).1);
        // Signed zero is a valid provider correction. A >=0 ceiling predicate
        // would divide by -0 and introduce an infinite limiter/NaN candidate.
        for zero in [0., -0.] {
            let (unchanged, damped) = limited_candidate(&plan, &[old], &[zero], 1.);
            assert_eq!(unchanged, vec![old]);
            assert!(!damped);
        }
    }
}

#[cfg(test)]
mod survivor_norm_contracts {
    use super::*;
    #[test]
    fn scaled_updates_reject_either_inconsistent_dimension() {
        let plan = crate::compile::compile(
            &crate::netlist::parse("title\nV1 out 0 1m\nD1 out 0 1p 25m").unwrap(),
        )
        .unwrap();
        assert!(scaled_update_norm(&plan, &[], &[], Default::default()).is_err());
        assert!(scaled_update_norm(&plan, &[0., 0.], &[0.], Default::default()).is_err());
    }
    #[test]
    fn equality_at_limiter_ceiling_and_backtracking_flags_preserve_their_invariants() {
        let plan = crate::compile::compile(
            &crate::netlist::parse("title\nV1 out 0 1m\nD1 out 0 1p 25m").unwrap(),
        )
        .unwrap();
        let delta = [0.05, 0.125];
        let (candidate, damped) = limited_candidate(&plan, &[0., 0.], &delta, 1.);
        assert_eq!(candidate, delta);
        assert!(!damped);
        for alpha in [0.5, 0.25, 0.125] {
            let (_, damped) = limited_candidate(&plan, &[0., 0.], &delta, alpha);
            assert!(damped);
        }
    }
}

#[cfg(test)]
mod october29_contracts {
    use super::*;
    #[test]
    fn branch_current_update_uses_amperes_not_voltage_tolerance() {
        let plan =
            crate::compile::compile(&crate::netlist::parse("title\nV1 x 0 1\nR1 x 0 1").unwrap())
                .unwrap();
        let options = NewtonOptions {
            voltage_absolute_tolerance: 1.,
            current_absolute_tolerance: 0.01,
            relative_tolerance: 1e-6,
            ..Default::default()
        };
        // One voltage row then one branch-current row; 0.02 A is twice its bound.
        let norm = scaled_update_norm(&plan, &[0., 0.02], &[0., 0.], options).unwrap();
        assert!((norm - 0.02 / (0.01 * (1. + 1e-6))).abs() < 1e-15);
    }
}
