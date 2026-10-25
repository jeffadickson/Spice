//! Deterministic scalar sweep execution built on immutable request variants.

use crate::{
    api::{Manifest, Request, run_with_registry},
    error::{ErrorCode, SimError, SimResult},
    registry::BackendRegistry,
    sampling::{LogUniformSweepPlan, UniformSweepPlan},
    variation::{ScalarGrid, ScalarSelection, ScalarSweep},
};
use serde::Serialize;

/// One completed or failed point. A point failure does not hide later points.
#[derive(Debug, Serialize)]
pub struct SweepPoint {
    pub index: usize,
    pub selection: ScalarSelection,
    pub execution: &'static str,
    pub result: Option<Manifest>,
    pub error: Option<SimError>,
}

/// Complete in-memory scalar campaign envelope for programmatic consumers.
/// It retains the declared values, including duplicates, alongside every point.
#[derive(Debug, Serialize)]
pub struct ScalarSweepRun {
    pub schema_version: &'static str,
    pub base_case_id: String,
    pub sweep: ScalarSweep,
    pub points: Vec<SweepPoint>,
    pub summary: SweepSummary,
}

/// Materialize a recorded uniform recipe and execute it with the same explicit
/// point semantics as a caller-provided scalar sweep.
pub fn run_uniform_plan(
    request: &Request,
    plan: &UniformSweepPlan,
    registry: &BackendRegistry,
) -> SimResult<Vec<SweepPoint>> {
    run_scalar(request, &plan.materialize()?, registry)
}

/// Materialize a positive log-uniform recipe and execute it sequentially.
pub fn run_log_uniform_plan(
    request: &Request,
    plan: &LogUniformSweepPlan,
    registry: &BackendRegistry,
) -> SimResult<Vec<SweepPoint>> {
    run_scalar(request, &plan.materialize()?, registry)
}

/// Create a bounded stable case ID for a generated point. The original case ID
/// is preserved verbatim and the kind/index suffix distinguishes duplicates.
pub fn derived_case_id(base: &str, kind: char, index: usize) -> SimResult<String> {
    if base.is_empty() || base.len() > 220 || !matches!(kind, 's' | 'g') || index > 63 {
        return Err(SimError::invalid("invalid generated case identity"));
    }
    Ok(format!("{base}--{kind}{index:02}"))
}

/// A small, JSON-friendly accounting summary. Counts make partial campaign
/// outcomes explicit instead of forcing callers to infer failure from absent data.
#[derive(Debug, Default, Serialize, PartialEq, Eq)]
pub struct SweepSummary {
    pub total: usize,
    pub succeeded: usize,
    pub failed: usize,
}

impl SweepSummary {
    /// Summarize completed points; cancellation is represented by the operation
    /// error and therefore does not invent a count for an unstarted point.
    pub fn from_points(points: &[SweepPoint]) -> Self {
        let succeeded = points
            .iter()
            .filter(|point| point.execution == "succeeded")
            .count();
        Self {
            total: points.len(),
            succeeded,
            failed: points.len() - succeeded,
        }
    }
}

/// Read a final real-valued signal from a successful point without requiring
/// JSON re-parsing. AC-only points are reported as unavailable to the caller.
pub fn final_signal(point: &SweepPoint, signal_id: &str) -> SimResult<f64> {
    let result = point
        .result
        .as_ref()
        .ok_or_else(|| SimError::invalid("sweep point did not succeed"))?;
    let index = result
        .signals
        .iter()
        .position(|signal| signal.id == signal_id)
        .ok_or_else(|| SimError::invalid("signal is not recorded"))?;
    let sample = result
        .samples
        .last()
        .ok_or_else(|| SimError::invalid("sweep point has no real samples"))?;
    sample
        .values
        .get(index)
        .copied()
        .ok_or_else(|| SimError::invalid("malformed recorded signal row"))
}

/// One query row that keeps a failed point visible as an unavailable value.
#[derive(Debug, Serialize)]
pub struct SignalRow {
    pub index: usize,
    pub selection: ScalarSelection,
    pub value: Option<f64>,
    pub reason: Option<String>,
}

/// Project a final signal across all points without silently dropping failures.
pub fn final_signal_rows(points: &[SweepPoint], signal_id: &str) -> Vec<SignalRow> {
    points
        .iter()
        .map(|point| match final_signal(point, signal_id) {
            Ok(value) => SignalRow {
                index: point.index,
                selection: point.selection.clone(),
                value: Some(value),
                reason: None,
            },
            Err(error) => SignalRow {
                index: point.index,
                selection: point.selection.clone(),
                value: None,
                reason: Some(error.message),
            },
        })
        .collect()
}

/// Project a named engine measurement across all completed points. The returned
/// unit follows the engine measurement and unavailable values retain a reason.
#[derive(Debug, Serialize)]
pub struct MeasurementRow {
    pub index: usize,
    pub selection: ScalarSelection,
    pub unit: Option<String>,
    pub value: Option<f64>,
    pub reason: Option<String>,
}

pub fn measurement_rows(points: &[SweepPoint], measurement_id: &str) -> Vec<MeasurementRow> {
    points
        .iter()
        .map(|point| {
            let measurement = point
                .result
                .as_ref()
                .and_then(|result| result.measurements.iter().find(|m| m.id == measurement_id));
            match measurement {
                Some(measurement) => MeasurementRow {
                    index: point.index,
                    selection: point.selection.clone(),
                    unit: Some(measurement.unit.clone()),
                    value: measurement.value,
                    reason: measurement.reason.clone(),
                },
                None => MeasurementRow {
                    index: point.index,
                    selection: point.selection.clone(),
                    unit: None,
                    value: None,
                    reason: Some("measurement is unavailable for sweep point".into()),
                },
            }
        })
        .collect()
}

/// Count assertion outcomes without treating indeterminate as a passing result.
#[derive(Debug, Default, Serialize, PartialEq, Eq)]
pub struct AssertionSummary {
    pub pass: usize,
    pub fail: usize,
    pub indeterminate: usize,
    pub missing: usize,
}

pub fn assertion_summary(points: &[SweepPoint], assertion_id: &str) -> AssertionSummary {
    let mut summary = AssertionSummary::default();
    for point in points {
        let outcome = point
            .result
            .as_ref()
            .and_then(|result| result.assertions.iter().find(|a| a.id == assertion_id));
        match outcome.map(|assertion| &assertion.outcome) {
            Some(crate::assertion::Outcome::Pass) => summary.pass += 1,
            Some(crate::assertion::Outcome::Fail) => summary.fail += 1,
            Some(crate::assertion::Outcome::Indeterminate) => summary.indeterminate += 1,
            None => summary.missing += 1,
        }
    }
    summary
}

/// A grid execution point retaining values in declared dimension order.
#[derive(Debug, Serialize)]
pub struct GridPoint {
    pub index: usize,
    pub selections: Vec<ScalarSelection>,
    pub execution: &'static str,
    pub result: Option<Manifest>,
    pub error: Option<SimError>,
}

/// Complete in-memory Cartesian campaign envelope.
#[derive(Debug, Serialize)]
pub struct ScalarGridRun {
    pub schema_version: &'static str,
    pub base_case_id: String,
    pub grid: ScalarGrid,
    pub points: Vec<GridPoint>,
    pub summary: GridSummary,
}

/// Accounting summary for a Cartesian-grid execution.
#[derive(Debug, Default, Serialize, PartialEq, Eq)]
pub struct GridSummary {
    pub total: usize,
    pub succeeded: usize,
    pub failed: usize,
}

impl GridSummary {
    pub fn from_points(points: &[GridPoint]) -> Self {
        let succeeded = points
            .iter()
            .filter(|point| point.execution == "succeeded")
            .count();
        Self {
            total: points.len(),
            succeeded,
            failed: points.len() - succeeded,
        }
    }
}

/// Run a bounded Cartesian grid sequentially. This intentionally does not claim
/// parallel scheduling or arbitrary mid-analysis checkpoint/restart semantics.
pub fn run_grid(
    request: &Request,
    grid: &ScalarGrid,
    registry: &BackendRegistry,
) -> SimResult<Vec<GridPoint>> {
    if request.case_id.is_empty() || request.case_id.len() > 220 {
        return Err(SimError::invalid("invalid grid base case ID"));
    }
    let mut points = Vec::new();
    for (index, (values, circuit)) in grid.circuits(&request.circuit)?.into_iter().enumerate() {
        let mut variant = request.clone();
        variant.case_id = derived_case_id(&request.case_id, 'g', index)?;
        variant.circuit = circuit;
        let selections = grid
            .dimensions
            .iter()
            .zip(&values)
            .map(|(dimension, value)| dimension.selection(&request.circuit, *value))
            .collect::<SimResult<Vec<_>>>()?;
        match run_with_registry(&variant, registry) {
            Ok(result) => points.push(GridPoint {
                index,
                selections,
                execution: "succeeded",
                result: Some(result),
                error: None,
            }),
            Err(error) => points.push(GridPoint {
                index,
                selections,
                execution: "failed",
                result: None,
                error: Some(error),
            }),
        }
    }
    Ok(points)
}

/// Execute a grid and retain its declared dimensions in the output envelope.
pub fn execute_grid(
    request: &Request,
    grid: &ScalarGrid,
    registry: &BackendRegistry,
) -> SimResult<ScalarGridRun> {
    let points = run_grid(request, grid, registry)?;
    let summary = GridSummary::from_points(&points);
    Ok(ScalarGridRun {
        schema_version: "1",
        base_case_id: request.case_id.clone(),
        grid: grid.clone(),
        points,
        summary,
    })
}

/// Execute each declared value in order. The generated case identity includes its
/// zero-based index so duplicate values remain distinguishable and reproducible.
pub fn run_scalar(
    request: &Request,
    sweep: &ScalarSweep,
    registry: &BackendRegistry,
) -> SimResult<Vec<SweepPoint>> {
    run_scalar_cancellable(request, sweep, registry, &false.into())
}

/// Execute a scalar sweep and return a self-describing serializable envelope.
pub fn execute_scalar(
    request: &Request,
    sweep: &ScalarSweep,
    registry: &BackendRegistry,
) -> SimResult<ScalarSweepRun> {
    let points = run_scalar(request, sweep, registry)?;
    let summary = SweepSummary::from_points(&points);
    Ok(ScalarSweepRun {
        schema_version: "1",
        base_case_id: request.case_id.clone(),
        sweep: sweep.clone(),
        points,
        summary,
    })
}

/// As [`run_scalar`], with cooperative cancellation checked only between complete
/// points. A returned vector never contains a partially executed point.
pub fn run_scalar_cancellable(
    request: &Request,
    sweep: &ScalarSweep,
    registry: &BackendRegistry,
    cancel: &std::sync::atomic::AtomicBool,
) -> SimResult<Vec<SweepPoint>> {
    let circuits = sweep.circuits(&request.circuit)?;
    if request.case_id.is_empty() || request.case_id.len() > 220 {
        return Err(SimError::invalid("invalid sweep base case ID"));
    }
    let mut points = Vec::with_capacity(circuits.len());
    for (index, (value, circuit)) in sweep.values.iter().zip(circuits).enumerate() {
        if cancel.load(std::sync::atomic::Ordering::Relaxed) {
            return Err(SimError::new(
                ErrorCode::Cancelled,
                "sweep cancelled between points",
            ));
        }
        let mut variant = request.clone();
        variant.case_id = derived_case_id(&request.case_id, 's', index)?;
        variant.circuit = circuit;
        let selection = sweep.selection(&request.circuit, *value)?;
        let point = match run_with_registry(&variant, registry) {
            Ok(result) => SweepPoint {
                index,
                selection,
                execution: "succeeded",
                result: Some(result),
                error: None,
            },
            Err(error) => SweepPoint {
                index,
                selection,
                execution: "failed",
                result: None,
                error: Some(error),
            },
        };
        points.push(point);
    }
    if points.len() > 64 {
        return Err(SimError::new(
            ErrorCode::ResourceExhausted,
            "sweep point limit exceeded",
        ));
    }
    Ok(points)
}
