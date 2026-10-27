//! Explicit bounded charge laws shared by device composition and verification.
use crate::{
    error::{SimError, SimResult},
    model::{CapacitorModel, TwoTerminalModel},
};

/// P-to-N terminal charge, its voltage derivative and stored energy in SI.
#[derive(Debug, Clone, Copy)]
pub struct ChargeContribution {
    /// Signed charge on the positive terminal in coulombs.
    pub charge_coulombs: f64,
    /// dQ/dV in farads; positive for this passive constant-capacitance profile.
    pub capacitance_farads: f64,
    /// Nonnegative stored energy in joules.
    pub energy_joules: f64,
}

/// Evaluate Q=C*V and E=C*V^2/2 with an explicit positive capacitance.
/// This is a constant parallel charge law; it has no depletion or transport
/// dependence. Reject unrepresentable contributions rather than clipping them.
pub fn constant_capacitance(farads: f64, voltage: f64) -> SimResult<ChargeContribution> {
    let capacitance = CapacitorModel(farads).coefficients()?.capacitance;
    if !voltage.is_finite() {
        return Err(SimError::invalid("nonfinite charge terminal voltage"));
    }
    let charge = capacitance * voltage;
    // Reuse Q and halve V before multiplication: squaring a large V first
    // can overflow even when a small C makes the physical energy representable.
    let energy = charge * (0.5 * voltage);
    if !charge.is_finite() || !energy.is_finite() {
        return Err(SimError::invalid("constant charge or energy overflow"));
    }
    Ok(ChargeContribution {
        charge_coulombs: charge,
        capacitance_farads: capacitance,
        energy_joules: energy,
    })
}
