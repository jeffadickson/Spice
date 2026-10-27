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

/// Explicit current/charge composition; the legacy Shockley parameters remain
/// unchanged. ConstantCharge is a parallel linear charge law, not a depletion model.
#[derive(Debug, Clone, Copy)]
pub enum DiodeProfile {
    Shockley(DiodeModel),
    ConstantCharge { conduction: DiodeModel, farads: f64 },
}
impl DiodeProfile {
    /// Validate both independent parameter domains before compilation.
    pub fn validate(self) -> SimResult<()> {
        self.conduction().validate()?;
        if let Self::ConstantCharge { farads, .. } = self {
            crate::charge::constant_capacitance(farads, 0.)?;
        }
        Ok(())
    }
    /// The unchanged conduction-current equation and its derivative. Charge
    /// finiteness is checked here; its dynamic Jacobian is stamped separately in Q.
    pub fn linearize(self, voltage: f64) -> SimResult<DiodeLinearization> {
        if let Self::ConstantCharge { farads, .. } = self {
            crate::charge::constant_capacitance(farads, voltage)?;
        }
        self.conduction().linearize(voltage)
    }
    /// Existing thermal-voltage trial limiting applies to the conduction equation.
    pub fn thermal_volts(self) -> f64 {
        self.conduction().thermal_volts
    }
    /// Explicit constant parallel capacitance, absent for a legacy diode.
    pub fn capacitance(self) -> Option<f64> {
        match self {
            Self::Shockley(_) => None,
            Self::ConstantCharge { farads, .. } => Some(farads),
        }
    }
    fn conduction(self) -> DiodeModel {
        match self {
            Self::Shockley(model) => model,
            Self::ConstantCharge { conduction, .. } => conduction,
        }
    }
}

/// A locally linearized diode current contribution at a trial terminal voltage.
#[derive(Debug, Clone, Copy)]
pub struct DiodeLinearization {
    pub current_amps: f64,
    pub conductance_siemens: f64,
}

impl DiodeModel {
    /// Validates the immutable Shockley parameters before an instance is bound
    /// or evaluated. Both quantities are SI scalars and must remain positive:
    /// a zero or negative value has no physical/equation meaning in this
    /// bounded model profile.
    pub fn validate(self) -> SimResult<()> {
        positive(self.saturation_amps, "diode saturation current")?;
        positive(self.thermal_volts, "diode thermal voltage")?;
        Ok(())
    }

    /// Validate parameters and evaluate the Shockley current/Jacobian.
    ///
    /// The caller must voltage-limit trial values before this method. The finite
    /// exponential guard prevents a numerical overflow becoming silent circuit data.
    pub fn linearize(self, voltage: f64) -> SimResult<DiodeLinearization> {
        self.validate()?;
        let isat = self.saturation_amps;
        let vt = self.thermal_volts;
        if !voltage.is_finite() || voltage / vt > 700. {
            return Err(crate::error::SimError::invalid(
                "diode trial voltage exceeds exponential domain",
            ));
        }
        let exponential = (voltage / vt).exp();
        // exp_m1 preserves the small-signal current when exp(v/Vt) rounds
        // to one; the separately evaluated exponential is the exact derivative.
        let current_amps = isat * (voltage / vt).exp_m1();
        let conductance_siemens = isat * exponential / vt;
        if !current_amps.is_finite() || !conductance_siemens.is_finite() {
            return Err(crate::error::SimError::invalid(
                "nonfinite diode contribution",
            ));
        }
        Ok(DiodeLinearization {
            current_amps,
            conductance_siemens,
        })
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
