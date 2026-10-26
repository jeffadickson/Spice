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
    /// Delayed, exponentially damped sinusoid with a phase in radians.
    DampedSine {
        offset: f64,
        amplitude: f64,
        frequency_hz: f64,
        delay_s: f64,
        damping_per_s: f64,
        phase_rad: f64,
    },
    /// Bounded SPICE-style pulse with explicit edge durations in seconds.
    Pulse {
        low: f64,
        high: f64,
        delay_s: f64,
        rise_s: f64,
        fall_s: f64,
        width_s: f64,
        period_s: f64,
    },
    /// SPICE EXP source: an exponential rise followed by an exponential return.
    Exponential {
        initial: f64,
        pulsed: f64,
        rise_delay_s: f64,
        rise_tau_s: f64,
        fall_delay_s: f64,
        fall_tau_s: f64,
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
            Self::DampedSine {
                offset,
                amplitude,
                frequency_hz,
                delay_s,
                damping_per_s,
                phase_rad,
            } if !offset.is_finite()
                || !amplitude.is_finite()
                || !frequency_hz.is_finite()
                || *frequency_hz <= 0.
                || !delay_s.is_finite()
                || *delay_s < 0.
                || !damping_per_s.is_finite()
                || *damping_per_s < 0.
                || !phase_rad.is_finite() =>
            {
                Err(SimError::invalid("invalid damped sine parameters"))
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
            Self::Pulse {
                low,
                high,
                delay_s,
                rise_s,
                fall_s,
                width_s,
                period_s,
            } if !low.is_finite()
                || !high.is_finite()
                || !delay_s.is_finite()
                || *delay_s < 0.
                || !rise_s.is_finite()
                || *rise_s < 0.
                || !fall_s.is_finite()
                || *fall_s < 0.
                || !width_s.is_finite()
                || *width_s < 0.
                || !period_s.is_finite()
                || *period_s <= 0.
                || rise_s + width_s + fall_s > *period_s =>
            {
                Err(SimError::invalid("invalid pulse parameters"))
            }
            Self::Exponential {
                initial,
                pulsed,
                rise_delay_s,
                rise_tau_s,
                fall_delay_s,
                fall_tau_s,
            } if !initial.is_finite()
                || !pulsed.is_finite()
                || !rise_delay_s.is_finite()
                || *rise_delay_s < 0.
                || !rise_tau_s.is_finite()
                || *rise_tau_s <= 0.
                || !fall_delay_s.is_finite()
                || *fall_delay_s < *rise_delay_s
                || !fall_tau_s.is_finite()
                || *fall_tau_s <= 0. =>
            {
                Err(SimError::invalid("invalid exponential source parameters"))
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
            Self::DampedSine {
                offset,
                amplitude,
                frequency_hz,
                delay_s,
                damping_per_s,
                phase_rad,
            } => {
                if time < *delay_s {
                    *offset
                } else {
                    let elapsed = time - delay_s;
                    let phase = std::f64::consts::TAU * frequency_hz * elapsed + phase_rad;
                    if !phase.is_finite() {
                        return Err(SimError::invalid("damped sine phase overflow"));
                    }
                    offset + amplitude * (-damping_per_s * elapsed).exp() * phase.sin()
                }
            }
            Self::Pulse {
                low,
                high,
                delay_s,
                rise_s,
                fall_s,
                width_s,
                period_s,
            } => {
                if time < *delay_s {
                    *low
                } else {
                    let phase = (time - delay_s).rem_euclid(*period_s);
                    if phase < *rise_s && *rise_s > 0. {
                        low + (high - low) * phase / rise_s
                    } else if phase < rise_s + width_s {
                        *high
                    } else if phase < rise_s + width_s + fall_s && *fall_s > 0. {
                        high + (low - high) * (phase - rise_s - width_s) / fall_s
                    } else {
                        *low
                    }
                }
            }
            Self::Exponential {
                initial,
                pulsed,
                rise_delay_s,
                rise_tau_s,
                fall_delay_s,
                fall_tau_s,
            } => {
                let rise = if time < *rise_delay_s {
                    0.
                } else {
                    1. - (-(time - rise_delay_s) / rise_tau_s).exp()
                };
                let fall = if time < *fall_delay_s {
                    0.
                } else {
                    1. - (-(time - fall_delay_s) / fall_tau_s).exp()
                };
                initial + (pulsed - initial) * (rise - fall)
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
            Self::Exponential {
                rise_delay_s,
                fall_delay_s,
                ..
            } => {
                vec![*rise_delay_s, *fall_delay_s]
            }
            Self::DampedSine { delay_s, .. } => vec![*delay_s],
            _ => Vec::new(),
        }
    }
}
