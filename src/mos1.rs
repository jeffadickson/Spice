//! Bounded Shichman-Hodges channel, derived from SpiceSharp 3.2.3 Level1/Biasing.cs.
//! MIT notice: third-party/SPICESHARP-LICENSE.txt. Body and overlap Q are separate.
use crate::error::{SimError, SimResult};
use serde::{Deserialize, Serialize};

/// Effective SI parameters at the evaluation temperature; beta already includes W/L.
#[derive(Debug, Clone, Copy, Serialize, Deserialize)]
#[serde(deny_unknown_fields)]
pub struct Mos1Model {
    pub p_channel: bool,
    /// Total intrinsic oxide capacitance Cox × effective width × effective length, in farads.
    #[serde(default)]
    pub oxide_farads: f64,
    pub threshold_volts: f64,
    pub beta_amps_per_volt_squared: f64,
    pub body_effect_sqrt_volts: f64,
    pub surface_potential_volts: f64,
    pub channel_length_modulation_per_volt: f64,
}
/// Drain-to-source current and terminal slopes ordered drain, gate, source, bulk.
/// Linearize returns exact piecewise slopes; spice_ac_linearize returns the
/// separate published small-signal body convention.
#[derive(Debug, Clone, Copy)]
pub struct Mos1Linearization {
    pub current_amps: f64,
    pub partials_siemens: [f64; 4],
}
impl Mos1Model {
    // SPICE continues sqrt(phi-Vbs) linearly for forward body bias, clamping
    // the continuation at zero. Return the exact negative slope with respect
    // to Vbs for Newton; the historical AC convention is handled separately.
    fn body_root(self, vbs: f64) -> SimResult<(f64, f64)> {
        if !vbs.is_finite() { return Err(SimError::invalid("nonfinite MOS1 body bias")); }
        let nominal=self.surface_potential_volts.sqrt();
        let root=if vbs<=0. { (self.surface_potential_volts-vbs).sqrt() }
            else if vbs>=2.*self.surface_potential_volts {0.}
            else { (nominal-vbs/(2.*nominal)).max(0.) };
        let slope=if root==0. {0.} else if vbs>0. {1./(2.*nominal)} else {1./(2.*root)};
        if !root.is_finite() || !slope.is_finite() {
            return Err(SimError::invalid("nonfinite MOS1 body continuation"));
        }
        Ok((root,slope))
    }

    /// Reject invalid physical parameters before evaluating any trial.
    pub fn validate(self) -> SimResult<()> {
        if !self.oxide_farads.is_finite()
            || self.oxide_farads < 0.
            || !self.threshold_volts.is_finite()
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
    /// Full Meyer capacitances ordered gate-source, gate-drain, gate-bulk.
    /// The effective source follows channel polarity and reverses with Vds.
    pub fn meyer(self, volts: [f64; 4]) -> SimResult<[f64; 3]> {
        self.validate()?;
        if self.oxide_farads == 0. {
            return Ok([0.; 3]);
        }
        let [d, g, s, b] = volts;
        let polarity = if self.p_channel { -1. } else { 1. };
        let reverse = polarity * (d - s) < 0.;
        let source = if reverse { d } else { s };
        let phi = self.surface_potential_volts;
        let vbs = polarity * (b - source);
        let threshold = polarity * self.threshold_volts
            + self.body_effect_sqrt_volts * (self.body_root(vbs)?.0 - phi.sqrt());
        let overdrive = polarity * (g - source) - threshold;
        let vds = (d - s).abs();
        if !overdrive.is_finite() || !vds.is_finite() {
            return Err(SimError::invalid("nonfinite Meyer bias"));
        }
        let vdsat = overdrive.max(0.025);
        let bulk = (-overdrive / phi).clamp(0., 1.);
        let inversion = (1. - 2. * bulk).max(0.) * (2. / 3.);
        let (source_fraction, drain_fraction) = if vds >= vdsat {
            (inversion, 0.)
        } else {
            let ratio = vds / vdsat;
            let denominator = 2. - ratio;
            (
                inversion * (1. - ((1. - ratio) / denominator).powi(2)),
                inversion * (1. - (1. / denominator).powi(2)),
            )
        };
        let mut caps =
            [source_fraction, drain_fraction, bulk].map(|fraction| fraction * self.oxide_farads);
        if reverse {
            caps.swap(0, 1);
        }
        Ok(caps)
    }

    /// Evaluate cutoff, linear or saturation region with source/drain reversal.
    /// Forward body bias uses the clamped linear continuation of sqrt(phi-Vbs).
    /// These are exact piecewise DC/transient derivatives; use spice_ac_linearize
    /// for the distinct historical forward-body small-signal convention.
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
        let (root,body_slope) = self.body_root(vbs)?;
        let threshold = polarity * self.threshold_volts
            + self.body_effect_sqrt_volts * (root - self.surface_potential_volts.sqrt());
        let overdrive = polarity * (g - source) - threshold;
        if !vds.is_finite() || !overdrive.is_finite() {
            return Err(SimError::invalid("nonfinite MOS1 channel bias"));
        }
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
        let gb = gm * self.body_effect_sqrt_volts * body_slope;
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

    /// ngspice-compatible AC channel slopes. Under forward body bias SPICE uses
    /// gamma/(2*sarg), rather than the exact derivative of its linear threshold
    /// continuation. Keep that convention out of Newton's residual Jacobian.
    pub fn spice_ac_linearize(self, volts: [f64;4]) -> SimResult<Mos1Linearization> {
        let mut result=self.linearize(volts)?;
        let [d,_,s,b]=volts;
        let polarity=if self.p_channel {-1.} else {1.};
        let reverse=polarity*(d-s)<0.;
        let vbs=polarity*(b-if reverse {d}else{s});
        let gm=result.partials_siemens[1].abs();
        if vbs<=0. || self.body_effect_sqrt_volts==0. || gm==0. { return Ok(result); }
        let (root,exact_slope)=self.body_root(vbs)?;
        let ac_slope=if root>0. {1./(2.*root)} else {0.};
        let difference=gm*self.body_effect_sqrt_volts*(ac_slope-exact_slope);
        if reverse {
            result.partials_siemens[0]+=difference;
            result.partials_siemens[3]-=difference;
        } else {
            result.partials_siemens[2]-=difference;
            result.partials_siemens[3]+=difference;
        }
        if result.partials_siemens.iter().any(|v|!v.is_finite()) {
            return Err(SimError::invalid("nonfinite MOS1 AC body slope"));
        }
        Ok(result)
    }
}
