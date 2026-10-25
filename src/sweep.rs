//! Deterministic scalar sweep execution built on immutable request variants.

use crate::{
    api::{Manifest, Request, run_with_registry},
    error::{ErrorCode, SimError, SimResult},
    registry::BackendRegistry,
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

/// A grid execution point retaining values in declared dimension order.
#[derive(Debug, Serialize)]
pub struct GridPoint {
    pub index: usize,
    pub selections: Vec<ScalarSelection>,
    pub execution: &'static str,
    pub result: Option<Manifest>,
    pub error: Option<SimError>,
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
        let succeeded = points.iter().filter(|point| point.execution == "succeeded").count();
        Self { total: points.len(), succeeded, failed: points.len() - succeeded }
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
        let selections = grid.dimensions.iter().zip(&values)
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

/// Execute each declared value in order. The generated case identity includes its
/// zero-based index so duplicate values remain distinguishable and reproducible.
pub fn run_scalar(
    request: &Request,
    sweep: &ScalarSweep,
    registry: &BackendRegistry,
) -> SimResult<Vec<SweepPoint>> {
    run_scalar_cancellable(request, sweep, registry, &false.into())
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
