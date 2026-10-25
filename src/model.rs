use crate::{error::SimResult, quantity::positive};
/// Minimal built-in linear-device contract; native plugin ABI and nonlinear states are deferred.
#[derive(Debug, Clone, Copy)]
pub struct LinearContribution {
    pub conductance: f64,
    pub capacitance: f64,
}
pub trait TwoTerminalModel {
    fn coefficients(&self) -> SimResult<LinearContribution>;
}
pub struct ResistorModel(pub f64);
pub struct CapacitorModel(pub f64);
/// Shockley diode parameters in SI units. Current is positive from p to n.
#[derive(Debug, Clone, Copy)]
pub struct DiodeModel {
    pub saturation_amps: f64,
    pub thermal_volts: f64,
}

/// A locally linearized diode current contribution at a trial terminal voltage.
#[derive(Debug, Clone, Copy)]
pub struct DiodeLinearization {
    pub current_amps: f64,
    pub conductance_siemens: f64,
}

impl DiodeModel {
    /// Validate parameters and evaluate the Shockley current/Jacobian.
    ///
    /// The caller must voltage-limit trial values before this method. The finite
    /// exponential guard prevents a numerical overflow becoming silent circuit data.
    pub fn linearize(self, voltage: f64) -> SimResult<DiodeLinearization> {
        let isat = positive(self.saturation_amps, "diode saturation current")?;
        let vt = positive(self.thermal_volts, "diode thermal voltage")?;
        if !voltage.is_finite() || voltage / vt > 700. {
            return Err(crate::error::SimError::invalid("diode trial voltage exceeds exponential domain"));
        }
        let exponential = (voltage / vt).exp();
        let current_amps = isat * (exponential - 1.0);
        let conductance_siemens = isat * exponential / vt;
        if !current_amps.is_finite() || !conductance_siemens.is_finite() {
            return Err(crate::error::SimError::invalid("nonfinite diode contribution"));
        }
        Ok(DiodeLinearization { current_amps, conductance_siemens })
    }
}
impl TwoTerminalModel for ResistorModel {
    fn coefficients(&self) -> SimResult<LinearContribution> {
        let r = positive(self.0, "resistance")?;
        Ok(LinearContribution {
            conductance: 1.0 / r,
            capacitance: 0.0,
        })
    }
}
impl TwoTerminalModel for CapacitorModel {
    fn coefficients(&self) -> SimResult<LinearContribution> {
        let c = positive(self.0, "capacitance")?;
        Ok(LinearContribution {
            conductance: 0.0,
            capacitance: c,
        })
    }
}
