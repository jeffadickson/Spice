//! Bounded Shichman-Hodges channel, derived from SpiceSharp 3.2.3 Level1/Biasing.cs.
//! MIT notice: third-party/SPICESHARP-LICENSE.txt. Body and overlap Q are separate.
use crate::error::{SimError, SimResult};
use serde::{Deserialize, Serialize};

/// Effective SI parameters at the evaluation temperature; beta already includes W/L.
#[derive(Debug, Clone, Copy, Serialize, Deserialize)]
#[serde(deny_unknown_fields)]
pub struct Mos1Model {
    pub p_channel: bool,
    pub threshold_volts: f64,
    pub beta_amps_per_volt_squared: f64,
    pub body_effect_sqrt_volts: f64,
    pub surface_potential_volts: f64,
    pub channel_length_modulation_per_volt: f64,
}
/// Drain-to-source current and exact partials ordered drain, gate, source, bulk.
#[derive(Debug, Clone, Copy)]
pub struct Mos1Linearization {
    pub current_amps: f64,
    pub partials_siemens: [f64; 4],
}
impl Mos1Model {
    /// Reject invalid physical parameters before evaluating any trial.
    pub fn validate(self) -> SimResult<()> {
        if !self.threshold_volts.is_finite()
            || !self.beta_amps_per_volt_squared.is_finite()
            || self.beta_amps_per_volt_squared <= 0.
            || !self.body_effect_sqrt_volts.is_finite()
            || self.body_effect_sqrt_volts < 0.
            || !self.surface_potential_volts.is_finite()
            || self.surface_potential_volts <= 0.
            || !self.channel_length_modulation_per_volt.is_finite()
            || self.channel_length_modulation_per_volt < 0.
        {
            return Err(SimError::invalid("invalid bounded MOS1 channel parameters"));
        }
        Ok(())
    }
    /// Evaluate cutoff, linear or saturation region with source/drain reversal.
    /// With nonzero GAMMA, forward effective body bias is outside this profile:
    /// SPICE's forward continuation and its AC approximation are not the same derivative.
    pub fn linearize(self, volts: [f64; 4]) -> SimResult<Mos1Linearization> {
        self.validate()?;
        if volts.iter().any(|v| !v.is_finite()) {
            return Err(SimError::invalid("nonfinite MOS1 terminal voltage"));
        }
        let [d, g, s, b] = volts;
        let polarity = if self.p_channel { -1. } else { 1. };
        let reverse = polarity * (d - s) < 0.;
        let source = if reverse { d } else { s };
        let vds = (polarity * (d - s)).abs();
        let vbs = polarity * (b - source);
        if self.body_effect_sqrt_volts > 0. && vbs > 0. {
            return Err(SimError::invalid(
                "bounded MOS1 requires nonpositive effective body bias when GAMMA is nonzero",
            ));
        }
        let root = (self.surface_potential_volts - vbs.min(0.)).sqrt();
        let threshold = polarity * self.threshold_volts
            + self.body_effect_sqrt_volts * (root - self.surface_potential_volts.sqrt());
        let overdrive = polarity * (g - source) - threshold;
        let (current, gm, gd) = if overdrive <= 0. {
            (0., 0., 0.)
        } else {
            let beta = self.beta_amps_per_volt_squared;
            let lambda = self.channel_length_modulation_per_volt;
            let effective = beta * (1. + lambda * vds);
            if overdrive <= vds {
                (
                    0.5 * effective * overdrive * overdrive,
                    effective * overdrive,
                    0.5 * beta * lambda * overdrive * overdrive,
                )
            } else {
                (
                    effective * vds * (overdrive - 0.5 * vds),
                    effective * vds,
                    effective * (overdrive - vds) + beta * lambda * vds * (overdrive - 0.5 * vds),
                )
            }
        };
        let gb = gm * self.body_effect_sqrt_volts / (2. * root);
        let result = if reverse {
            Mos1Linearization {
                current_amps: -polarity * current,
                partials_siemens: [gd + gm + gb, -gm, -gd, -gb],
            }
        } else {
            Mos1Linearization {
                current_amps: polarity * current,
                partials_siemens: [gd, gm, -(gd + gm + gb), gb],
            }
        };
        if !result.current_amps.is_finite()
            || result.partials_siemens.iter().any(|v| !v.is_finite())
        {
            return Err(SimError::invalid("nonfinite MOS1 channel contribution"));
        }
        Ok(result)
    }
}
