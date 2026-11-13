//! Bounded SPICE junction charge adapted from SpiceSharp 3.2.3 Dynamic.cs.
//! MIT upstream notice is retained in third-party/SPICESHARP-LICENSE.txt.
//! Parameters are effective values at the evaluation temperature, in SI units.
use crate::{
    error::{SimError, SimResult},
    model::{DiodeLinearization, DiodeModel},
};
use serde::{Deserialize, Serialize};

/// Validated junction law. Series resistance is represented by explicit topology;
/// temperature scaling and avalanche parameters are separate qualification work.
#[derive(Debug, Clone, Copy, Serialize, Deserialize)]
#[serde(deny_unknown_fields)]
pub struct SpiceDiodeModel {
    pub saturation_amps: f64,
    /// Emission coefficient multiplied by kT/q, in volts.
    pub thermal_volts: f64,
    pub junction_farads: f64,
    pub junction_volts: f64,
    pub grading: f64,
    pub forward_coefficient: f64,
    pub transit_seconds: f64,
}
/// Junction charge and its full scalar derivative. No electrostatic energy is
/// inferred from transport charge; unavailable energy audits remain explicit.
#[derive(Debug, Clone, Copy)]
pub struct JunctionCharge {
    pub charge_coulombs: f64,
    pub capacitance_farads: f64,
}
/// Normalized BC control for a forward Early charge companion. Terminals use
/// the same polarity as the normalized BE branch; volts is strictly positive.
#[derive(Debug, Clone, Copy, Serialize, Deserialize)]
#[serde(deny_unknown_fields)]
pub struct EarlyChargeControl {
    pub cp: crate::ids::NodeId,
    pub cn: crate::ids::NodeId,
    pub volts: f64,
}
/// Complete two-input charge tangent, in coulombs and farads.
#[derive(Debug, Clone, Copy)]
pub struct CoupledJunctionCharge {
    pub charge_coulombs: f64,
    pub capacitance_farads: f64,
    pub control_farads: f64,
}
impl SpiceDiodeModel {
    /// VAF-only low-injection GP forward diffusion charge. The positive-bias
    /// branch follows ngspice's TF*If/qb with qb=1/(1-Vbc/VAF). Depletion is
    /// unaffected. Reverse BE retains TF*If and no BC coupling. No clipping or
    /// hidden Gmin enters this constitutive law.
    pub fn early_charge(self, be: f64, bc: f64, early_volts: f64) -> SimResult<CoupledJunctionCharge> {
        if !bc.is_finite() || !early_volts.is_finite() || early_volts <= 0.
            || !early_volts.recip().is_finite() || 1.-bc/early_volts <= 0. {
            return Err(SimError::invalid("invalid forward Early charge domain"));
        }
        let base=self.charge(be)?;
        let mut q=CoupledJunctionCharge{charge_coulombs:base.charge_coulombs,
            capacitance_farads:base.capacitance_farads,control_farads:0.};
        if be>0. && self.transit_seconds>0. {
            let i=self.linearize(be)?;
            let modulation=-bc/early_volts;
            q.charge_coulombs+=self.transit_seconds*i.current_amps*modulation;
            q.capacitance_farads+=self.transit_seconds*i.conductance_siemens*modulation;
            q.control_farads=-self.transit_seconds*i.current_amps/early_volts;
        }
        if [q.charge_coulombs,q.capacitance_farads,q.control_farads].iter().any(|v|!v.is_finite()) {
            return Err(SimError::invalid("forward Early charge overflow"));
        }
        Ok(q)
    }
    /// Reject unsupported mathematical domains rather than clipping physical data.
    pub fn validate(self) -> SimResult<()> {
        self.conduction().validate()?;
        if !self.junction_farads.is_finite()
            || self.junction_farads < 0.
            || !self.junction_volts.is_finite()
            || self.junction_volts <= 0.
            || !self.grading.is_finite()
            || !(0. ..1.).contains(&self.grading)
            || !self.forward_coefficient.is_finite()
            || !(0. ..1.).contains(&self.forward_coefficient)
            || !self.transit_seconds.is_finite()
            || self.transit_seconds < 0.
        {
            return Err(SimError::invalid(
                "invalid SPICE junction charge parameters",
            ));
        }
        Ok(())
    }
    fn conduction(self) -> DiodeModel {
        DiodeModel {
            saturation_amps: self.saturation_amps,
            thermal_volts: self.thermal_volts,
        }
    }
    /// Physical conduction current and exact voltage derivative; no implicit Gmin.
    pub fn linearize(self, volts: f64) -> SimResult<DiodeLinearization> {
        self.validate()?;
        self.conduction().linearize(volts)
    }
    /// Depletion integral plus transport charge TT*I; derivative includes TT*dI/dV.
    /// Forward continuation is tangent-continuous at FC*VJ, following SPICE3.
    pub fn charge(self, volts: f64) -> SimResult<JunctionCharge> {
        let current = self.linearize(volts)?;
        let (c, vj, m, fc) = (
            self.junction_farads,
            self.junction_volts,
            self.grading,
            self.forward_coefficient,
        );
        let transition = fc * vj;
        let (depletion, capacitance) = if volts < transition {
            let arg = 1. - volts / vj;
            // exp_m1 avoids cancellation close to zero voltage.
            let q = -vj * c * ((1. - m) * arg.ln()).exp_m1() / (1. - m);
            (q, c * arg.powf(-m))
        } else {
            let f1 = -vj * ((1. - m) * (-fc).ln_1p()).exp_m1() / (1. - m);
            let f2 = (1. - fc).powf(1. + m);
            let f3 = 1. - fc * (1. + m);
            (
                c * f1
                    + c / f2
                        * (f3 * (volts - transition)
                            + m / (2. * vj) * (volts - transition) * (volts + transition)),
                c / f2 * (f3 + m * volts / vj),
            )
        };
        let q = depletion + self.transit_seconds * current.current_amps;
        let dq = capacitance + self.transit_seconds * current.conductance_siemens;
        if !q.is_finite() || !dq.is_finite() {
            return Err(SimError::invalid("SPICE junction charge overflow"));
        }
        // Transport storage is not generally a passive electrostatic energy law.
        // Energy audits must report NotChecked rather than interpret a placeholder.
        Ok(JunctionCharge {
            charge_coulombs: q,
            capacitance_farads: dq,
        })
    }
    pub fn has_charge(self) -> bool {
        self.junction_farads > 0. || self.transit_seconds > 0.
    }
}
