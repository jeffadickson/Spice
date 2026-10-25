use crate::{
    error::{SimError, SimResult},
    quantity::positive,
};
/// Logarithmic grid includes both endpoints and is bounded before allocation.
pub fn log_grid(start: f64, stop: f64, points: usize) -> SimResult<Vec<f64>> {
    positive(start, "start frequency")?;
    positive(stop, "stop frequency")?;
    if stop < start || !(2..=10_000).contains(&points) {
        return Err(SimError::invalid("invalid log grid bounds/count"));
    }
    Ok((0..points)
        .map(|i| {
            if i + 1 == points {
                stop
            } else {
                (start.ln() + (stop.ln() - start.ln()) * i as f64 / (points - 1) as f64).exp()
            }
        })
        .collect())
}
