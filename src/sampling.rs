//! Small deterministic sampling primitives for reproducible variation plans.

use crate::error::{SimError, SimResult};
use crate::variation::ScalarSweep;

/// Versioned metadata required to reproduce a pseudo-random materialization.
#[derive(Debug, Clone, PartialEq, Eq, serde::Serialize)]
pub struct SamplingProvenance {
    pub algorithm: &'static str,
    pub seed: u64,
}

impl SamplingProvenance {
    pub const fn splitmix64(seed: u64) -> Self {
        Self {
            algorithm: "splitmix64-v1",
            seed,
        }
    }
}

/// A specified inclusive-exclusive finite interval.
#[derive(Debug, Clone, Copy, serde::Serialize, serde::Deserialize)]
#[serde(deny_unknown_fields)]
pub struct UniformRange {
    pub low: f64,
    pub high: f64,
}

impl UniformRange {
    pub fn validate(self) -> SimResult<()> {
        if !self.low.is_finite() || !self.high.is_finite() || self.low >= self.high {
            return Err(SimError::invalid("invalid uniform sampling range"));
        }
        Ok(())
    }
}

/// A positive interval sampled uniformly in natural-logarithmic space.
#[derive(Debug, Clone, Copy, serde::Serialize, serde::Deserialize)]
#[serde(deny_unknown_fields)]
pub struct LogUniformRange {
    pub low: f64,
    pub high: f64,
}

impl LogUniformRange {
    pub fn validate(self) -> SimResult<()> {
        if !self.low.is_finite()
            || !self.high.is_finite()
            || self.low <= 0.
            || self.low >= self.high
        {
            return Err(SimError::invalid("invalid log-uniform sampling range"));
        }
        Ok(())
    }
}

/// A deterministic SplitMix64 stream. It is a reproducibility utility, not a
/// cryptographic random number generator or statistical-quality guarantee.
#[derive(Debug, Clone)]
pub struct SeededSequence {
    state: u64,
}

impl SeededSequence {
    pub const fn new(seed: u64) -> Self {
        Self { state: seed }
    }

    pub fn next_u64(&mut self) -> u64 {
        self.state = self.state.wrapping_add(0x9e37_79b9_7f4a_7c15);
        let mut z = self.state;
        z = (z ^ (z >> 30)).wrapping_mul(0xbf58_476d_1ce4_e5b9);
        z = (z ^ (z >> 27)).wrapping_mul(0x94d0_49bb_1331_11eb);
        z ^ (z >> 31)
    }

    /// Sample the half-open interval using the high 53 bits to match f64's
    /// representable mantissa precision without generating the upper bound.
    pub fn uniform(&mut self, range: UniformRange) -> SimResult<f64> {
        range.validate()?;
        let fraction = (self.next_u64() >> 11) as f64 * (1.0 / ((1u64 << 53) as f64));
        Ok(range.low + (range.high - range.low) * fraction)
    }
}

/// Generate a bounded reproducible sequence from a recorded seed and range.
pub fn uniform_values(seed: u64, count: usize, range: UniformRange) -> SimResult<Vec<f64>> {
    if count == 0 || count > 64 {
        return Err(SimError::invalid("sample count must be 1..64"));
    }
    range.validate()?;
    let mut stream = SeededSequence::new(seed);
    (0..count).map(|_| stream.uniform(range)).collect()
}

/// Generate positive samples evenly distributed in log space.
pub fn log_uniform_values(seed: u64, count: usize, range: LogUniformRange) -> SimResult<Vec<f64>> {
    range.validate()?;
    let values = uniform_values(
        seed,
        count,
        UniformRange {
            low: range.low.ln(),
            high: range.high.ln(),
        },
    )?;
    Ok(values.into_iter().map(f64::exp).collect())
}

/// Serialized recipe for a scalar sweep. Recording the recipe rather than only
/// generated values lets a caller reproduce a campaign independently.
#[derive(Debug, Clone, serde::Serialize, serde::Deserialize)]
#[serde(deny_unknown_fields)]
pub struct UniformSweepPlan {
    pub element_id: String,
    pub seed: u64,
    pub count: usize,
    pub range: UniformRange,
}

impl UniformSweepPlan {
    pub const fn provenance(&self) -> SamplingProvenance {
        SamplingProvenance::splitmix64(self.seed)
    }
    pub fn materialize(&self) -> SimResult<ScalarSweep> {
        let sweep = ScalarSweep {
            element_id: self.element_id.clone(),
            values: uniform_values(self.seed, self.count, self.range)?,
        };
        sweep.validate()?;
        Ok(sweep)
    }
}

/// Serialized log-uniform scalar sweep recipe for positive-valued devices.
#[derive(Debug, Clone, serde::Serialize, serde::Deserialize)]
#[serde(deny_unknown_fields)]
pub struct LogUniformSweepPlan {
    pub element_id: String,
    pub seed: u64,
    pub count: usize,
    pub range: LogUniformRange,
}

impl LogUniformSweepPlan {
    pub const fn provenance(&self) -> SamplingProvenance {
        SamplingProvenance::splitmix64(self.seed)
    }
    pub fn materialize(&self) -> SimResult<ScalarSweep> {
        let sweep = ScalarSweep {
            element_id: self.element_id.clone(),
            values: log_uniform_values(self.seed, self.count, self.range)?,
        };
        sweep.validate()?;
        Ok(sweep)
    }
}
