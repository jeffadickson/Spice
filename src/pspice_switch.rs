//! Bounded PSpice smooth VSWITCH constitutive law, SI units.
//! Control is high impedance. No hysteresis, clamps or threshold perturbations.
use crate::error::{SimError, SimResult};
/// Explicit resistance endpoints (ohms) and control thresholds (volts).
#[derive(Debug, Clone, Copy)]
pub struct SwitchModel {
    pub ron: f64,
    pub roff: f64,
    pub von: f64,
    pub voff: f64,
}
/// Current P-to-N and its output/control differential voltage derivatives.
#[derive(Debug, Clone, Copy)]
pub struct SwitchContribution {
    pub current: f64,
    pub output_siemens: f64,
    pub control_siemens: f64,
}
impl SwitchModel {
    /// Reject values outside this qualified profile rather than silently modifying a model.
    pub fn validate(&self) -> SimResult<()> {
        let span = self.von - self.voff;
        if [self.ron, self.roff, self.von, self.voff, span]
            .iter()
            .any(|v| !v.is_finite())
            || self.ron < 1e-3
            || self.roff < self.ron
            || self.roff > 1e12
            || span.abs() < 1e-12
        {
            return Err(SimError::invalid(
                "VSWITCH requires 1mOhm<=RON<=ROFF<=1TOhm and finite separated thresholds",
            ));
        }
        Ok(())
    }
    /// Smooth logarithmic resistance; endpoint control derivatives vanish. AC must
    /// use these same signed partials at the converged DC bias (ngspice bug883).
    pub fn linearize(&self, output_v: f64, control_v: f64) -> SimResult<SwitchContribution> {
        self.validate()?;
        if !output_v.is_finite() || !control_v.is_finite() {
            return Err(SimError::invalid("nonfinite switch voltage"));
        }
        let span = self.von - self.voff;
        // Compare thresholds before subtraction to avoid overflow on finite extremes.
        let t = if (span > 0. && control_v <= self.voff) || (span < 0. && control_v >= self.voff) {
            0.
        } else if (span > 0. && control_v >= self.von) || (span < 0. && control_v <= self.von) {
            1.
        } else {
            (control_v - self.voff) / span
        };
        let logarithm = self.ron.ln() - self.roff.ln();
        let h = t * t * (3. - 2. * t);
        let g = (-(self.roff.ln() + logarithm * h)).exp();
        let current = output_v * g;
        let derivative = -current * logarithm * (6. * t * (1. - t)) / span;
        if [g, current, derivative].iter().any(|v| !v.is_finite()) {
            return Err(SimError::invalid("switch contribution overflow"));
        }
        Ok(SwitchContribution {
            current,
            output_siemens: g,
            control_siemens: derivative,
        })
    }
}
