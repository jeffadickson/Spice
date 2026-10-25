//! Deterministic scalar sweep execution built on immutable request variants.

use crate::{
    api::{Manifest, Request, run_with_registry},
    error::{ErrorCode, SimError, SimResult},
    registry::BackendRegistry,
    variation::ScalarSweep,
};
use serde::Serialize;

/// One completed or failed point. A point failure does not hide later points.
#[derive(Debug, Serialize)]
pub struct SweepPoint {
    pub index: usize,
    pub value: f64,
    pub execution: &'static str,
    pub result: Option<Manifest>,
    pub error: Option<SimError>,
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
            return Err(SimError::new(ErrorCode::Cancelled, "sweep cancelled between points"));
        }
        let mut variant = request.clone();
        variant.case_id = format!("{}--s{:02}", request.case_id, index);
        variant.circuit = circuit;
        let point = match run_with_registry(&variant, registry) {
            Ok(result) => SweepPoint { index, value: *value, execution: "succeeded", result: Some(result), error: None },
            Err(error) => SweepPoint { index, value: *value, execution: "failed", result: None, error: Some(error) },
        };
        points.push(point);
    }
    if points.len() > 64 {
        return Err(SimError::new(ErrorCode::ResourceExhausted, "sweep point limit exceeded"));
    }
    Ok(points)
}
