use crate::error::{SimError, SimResult};
use serde::{Deserialize, Serialize};
#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(deny_unknown_fields)]
pub struct Point {
    pub time_s: f64,
    pub value: f64,
}
#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(tag = "kind", rename_all = "snake_case", deny_unknown_fields)]
pub enum Waveform {
    Constant {
        value: f64,
    },
    Pwl {
        points: Vec<Point>,
    },
    Sine {
        offset: f64,
        amplitude: f64,
        frequency_hz: f64,
        phase_rad: f64,
    },
}
impl Waveform {
    pub fn validate(&self) -> SimResult<()> {
        match self {
            Self::Constant { value } if !value.is_finite() => {
                Err(SimError::invalid("nonfinite constant source"))
            }
            Self::Sine {
                offset,
                amplitude,
                frequency_hz,
                phase_rad,
            } if !offset.is_finite()
                || !amplitude.is_finite()
                || !frequency_hz.is_finite()
                || *frequency_hz <= 0.
                || !phase_rad.is_finite() =>
            {
                Err(SimError::invalid("invalid sine parameters"))
            }
            Self::Pwl { points }
                if points.is_empty()
                    || points.len() > 4096
                    || points.iter().any(|p| {
                        !p.time_s.is_finite() || p.time_s < 0. || !p.value.is_finite()
                    })
                    || points.windows(2).any(|w| w[1].time_s <= w[0].time_s) =>
            {
                Err(SimError::invalid("invalid PWL points/order/budget"))
            }
            _ => Ok(()),
        }
    }
    pub fn at(&self, time: f64) -> SimResult<f64> {
        self.validate()?;
        self.at_validated(time)
    }
    /// BoundSources validates and freezes definitions once before repeated evaluation.
    pub(crate) fn at_validated(&self, time: f64) -> SimResult<f64> {
        if !time.is_finite() || time < 0. {
            return Err(SimError::invalid("invalid source time"));
        }
        let value = match self {
            Self::Constant { value } => *value,
            Self::Sine {
                offset,
                amplitude,
                frequency_hz,
                phase_rad,
            } => {
                let phase = std::f64::consts::TAU * frequency_hz * time + phase_rad;
                if !phase.is_finite() {
                    return Err(SimError::invalid("sine phase overflow"));
                }
                offset + amplitude * phase.sin()
            }
            Self::Pwl { points } => {
                let index = points.partition_point(|p| p.time_s <= time);
                if index == 0 {
                    points[0].value
                } else if index == points.len() {
                    points[index - 1].value
                } else {
                    let a = &points[index - 1];
                    let b = &points[index];
                    let f = (time - a.time_s) / (b.time_s - a.time_s);
                    (1. - f) * a.value + f * b.value
                }
            }
        };
        crate::quantity::finite(value, "source evaluation")
    }
    pub fn breakpoints(&self) -> Vec<f64> {
        match self {
            Self::Pwl { points } => points.iter().map(|p| p.time_s).collect(),
            _ => Vec::new(),
        }
    }
}
