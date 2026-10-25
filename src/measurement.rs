use crate::{
    error::{SimError, SimResult},
    transient::Sample,
};
/// Interpolate only within recorded samples; no extrapolation or silent zero substitution.
pub fn value_at(samples: &[Sample], signal: usize, time: f64) -> SimResult<f64> {
    if !time.is_finite() || samples.is_empty() {
        return Err(SimError::invalid("invalid measurement time/data"));
    }
    if samples
        .iter()
        .any(|s| signal >= s.values.len() || !s.time_s.is_finite() || !s.values[signal].is_finite())
        || samples.windows(2).any(|w| w[1].time_s <= w[0].time_s)
    {
        return Err(SimError::invalid("invalid sample axis or values"));
    }
    if time < samples[0].time_s || time > samples.last().unwrap().time_s {
        return Err(SimError::invalid("measurement outside recorded range"));
    }
    for sample in samples {
        if sample.time_s == time {
            return Ok(sample.values[signal]);
        }
    }
    for pair in samples.windows(2) {
        if pair[0].time_s < time && time < pair[1].time_s {
            let fraction = (time - pair[0].time_s) / (pair[1].time_s - pair[0].time_s);
            let value =
                (1.0 - fraction) * pair[0].values[signal] + fraction * pair[1].values[signal];
            return crate::quantity::finite(value, "interpolated value");
        }
    }
    Err(SimError::invalid("measurement has no bracketing samples"))
}
