//! Memoryless product of an existing branch current and a differential voltage.
//! This primitive alone is not a BJT model: coupled charge is a separate law.
use crate::error::{SimError, SimResult};

/// P-to-N output amperes, dimensionless branch-current partial, and control
/// voltage partial in siemens. Voltage coefficient is in inverse volts.
#[derive(Debug, Clone, Copy)]
pub struct ScaledCurrentContribution {
    pub current_amps: f64,
    pub current_gain: f64,
    pub control_siemens: f64,
}

/// Evaluate Iout=gain*Icontrol*(1+voltage_coefficient*Vcontrol), preserving
/// finite signed factors. Overflow is an error, never a clamp or zero fallback.
pub fn linearize(
    gain: f64,
    voltage_coefficient: f64,
    control_amps: f64,
    control_volts: f64,
) -> SimResult<ScaledCurrentContribution> {
    if [gain, voltage_coefficient, control_amps, control_volts]
        .iter()
        .any(|v| !v.is_finite())
    {
        return Err(SimError::invalid("nonfinite voltage-scaled CCCS input"));
    }
    let current_gain = gain * (1. + voltage_coefficient * control_volts);
    let current_amps = control_amps * current_gain;
    let control_siemens = (gain * control_amps) * voltage_coefficient;
    if [current_gain, current_amps, control_siemens]
        .iter()
        .any(|v| !v.is_finite())
    {
        return Err(SimError::invalid(
            "voltage-scaled CCCS contribution overflow",
        ));
    }
    Ok(ScaledCurrentContribution {
        current_amps,
        current_gain,
        control_siemens,
    })
}
