use crate::{
    error::{ErrorCode, SimError, SimResult},
    ids::NodeId,
};
use serde::{Deserialize, Serialize};
use std::collections::BTreeSet;
/// SI terminal conventions: currents flow p -> n, including source and inductor branches.
#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(tag = "kind", rename_all = "snake_case", deny_unknown_fields)]
pub enum Element {
    /// Memoryless I(P->N)=(Vp-Vn)/R(Vcp-Vcn), high-impedance control.
    BehavioralResistor {
        id: String, p: NodeId, n: NodeId, cp: NodeId, cn: NodeId,
        expression: crate::expression::Expr,
    },
    /// DC-only voltage relation Vp-Vn=expression(Vcp-Vcn), with P-to-N branch current.
    BehavioralVoltage {
        id: String, p: NodeId, n: NodeId, cp: NodeId, cn: NodeId,
        expression: crate::expression::Expr,
    },
    /// Current-only four-terminal MOS1 channel; body and overlap storage are explicit companions.
    Mos1Channel {
        id: String,
        p: NodeId,
        n: NodeId,
        gate: NodeId,
        bulk: NodeId,
        model: crate::mos1::Mos1Model,
    },
    /// Four terminal bulk BSIM3 3.3.0 device; D/G/S/B currents enter the model.
    Bsim330 {
        id: String,
        p: NodeId,
        n: NodeId,
        gate: NodeId,
        bulk: NodeId,
        model: crate::bsim330::Model,
    },
    /// Pinned static FD SOI NMOS profile with independent back gate E.
    BsimSoiFdReference {
        id: String,
        p: NodeId,
        n: NodeId,
        gate: NodeId,
        back_gate: NodeId,
    },
    /// Charge-only junction companion. Transport IS is a Q scale and contributes
    /// no conduction F; sense branches can therefore exclude displacement.
    JunctionStorage {
        id: String,
        p: NodeId,
        n: NodeId,
        model: crate::spice_diode::SpiceDiodeModel,
        /// Optional normalized BC coupling; absent retains the legacy wire law.
        #[serde(default, skip_serializing_if = "Option::is_none")]
        early_effect: Option<crate::spice_diode::EarlyChargeControl>,
    },
    /// SPICE depletion and diffusion charge evaluated with effective SI parameters.
    SpiceDiode {
        id: String,
        p: NodeId,
        n: NodeId,
        model: crate::spice_diode::SpiceDiodeModel,
    },
    Resistor {
        id: String,
        p: NodeId,
        n: NodeId,
        ohms: f64,
    },
    Capacitor {
        id: String,
        p: NodeId,
        n: NodeId,
        farads: f64,
    },
    Inductor {
        id: String,
        p: NodeId,
        n: NodeId,
        henries: f64,
    },
    Voltage {
        id: String,
        p: NodeId,
        n: NodeId,
        volts: f64,
    },
    Current {
        id: String,
        p: NodeId,
        n: NodeId,
        amps: f64,
    },
    /// Voltage-controlled current source; current p -> n equals siemens*(cp-cn).
    Vccs {
        id: String,
        p: NodeId,
        n: NodeId,
        cp: NodeId,
        cn: NodeId,
        siemens: f64,
    },
    /// Voltage-controlled voltage source; V(p)-V(n) equals gain*(V(cp)-V(cn)).
    Vcvs {
        id: String,
        p: NodeId,
        n: NodeId,
        cp: NodeId,
        cn: NodeId,
        gain: f64,
    },
    /// Smooth PSpice VSWITCH with ideal high-impedance differential control.
    PspiceSwitch {
        id: String,
        p: NodeId,
        n: NodeId,
        cp: NodeId,
        cn: NodeId,
        ron: f64,
        roff: f64,
        von: f64,
        voff: f64,
    },
    /// Current-controlled current source; current p -> n equals gain*i(control_id).
    Cccs {
        id: String,
        p: NodeId,
        n: NodeId,
        control_id: String,
        gain: f64,
    },
    /// High-impedance voltage modulation of a P-to-N branch current control.
    /// Iout=gain*i(control_id)*(1+voltage_coefficient*(Vcp-Vcn)).
    VoltageScaledCccs {
        id: String, p: NodeId, n: NodeId, cp: NodeId, cn: NodeId,
        control_id: String, gain: f64, voltage_coefficient: f64,
    },
    /// Current-controlled voltage source; V(p)-V(n) equals ohms*i(control_id).
    Ccvs {
        id: String,
        p: NodeId,
        n: NodeId,
        control_id: String,
        ohms: f64,
    },
    /// Shockley diode with current defined from p to n.
    Diode {
        id: String,
        p: NodeId,
        n: NodeId,
        saturation_amps: f64,
        thermal_volts: f64,
    },
    /// Unchanged Shockley conduction plus explicit constant parallel capacitance.
    DiodeWithCapacitance {
        id: String,
        p: NodeId,
        n: NodeId,
        saturation_amps: f64,
        thermal_volts: f64,
        farads: f64,
    },
}
impl Element {
    pub fn id(&self) -> &str {
        match self {
            Self::BsimSoiFdReference { id, .. }
            | Self::Bsim330 { id, .. }
            | Self::Mos1Channel { id, .. }
            | Self::Resistor { id, .. }
            | Self::JunctionStorage { id, .. }
            | Self::SpiceDiode { id, .. }
            | Self::Capacitor { id, .. }
            | Self::Inductor { id, .. }
            | Self::Voltage { id, .. }
            | Self::Current { id, .. }
            | Self::Vccs { id, .. }
            | Self::Vcvs { id, .. }
            | Self::BehavioralVoltage { id, .. }
            | Self::BehavioralResistor { id, .. }
            | Self::PspiceSwitch { id, .. }
            | Self::Cccs { id, .. }
            | Self::VoltageScaledCccs { id, .. }
            | Self::Ccvs { id, .. }
            | Self::Diode { id, .. }
            | Self::DiodeWithCapacitance { id, .. } => id,
        }
    }
    pub(crate) fn native_mos(&self) -> Option<([NodeId; 4], crate::bsim330::NativeModel)> {
        use crate::bsim330::NativeModel;
        match self {
            Self::Bsim330 {
                p,
                n,
                gate,
                bulk,
                model,
                ..
            } => Some(([*p, *gate, *n, *bulk], NativeModel::Bulk(*model))),
            Self::BsimSoiFdReference {
                p,
                n,
                gate,
                back_gate,
                ..
            } => Some(([*p, *gate, *n, *back_gate], NativeModel::SoiFd)),
            _ => None,
        }
    }

    pub fn terminals(&self) -> (NodeId, NodeId) {
        match self {
            Self::BsimSoiFdReference { p, n, .. }
            | Self::Bsim330 { p, n, .. }
            | Self::Mos1Channel { p, n, .. }
            | Self::Resistor { p, n, .. }
            | Self::JunctionStorage { p, n, .. }
            | Self::SpiceDiode { p, n, .. }
            | Self::Capacitor { p, n, .. }
            | Self::Inductor { p, n, .. }
            | Self::Voltage { p, n, .. }
            | Self::Current { p, n, .. }
            | Self::Vccs { p, n, .. }
            | Self::Vcvs { p, n, .. }
            | Self::BehavioralVoltage { p, n, .. }
            | Self::BehavioralResistor { p, n, .. }
            | Self::PspiceSwitch { p, n, .. }
            | Self::Cccs { p, n, .. }
            | Self::VoltageScaledCccs { p, n, .. }
            | Self::Ccvs { p, n, .. }
            | Self::Diode { p, n, .. }
            | Self::DiodeWithCapacitance { p, n, .. } => (*p, *n),
        }
    }
}
/// Explicit ground and stable ordered node identities; no mutable compiled-plan backdoor.
#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(deny_unknown_fields)]
pub struct Circuit {
    pub nodes: Vec<String>,
    pub elements: Vec<Element>,
}
impl Circuit {
    pub fn validate(&self) -> SimResult<()> {
        if self.nodes.first().map(String::as_str) != Some("0") {
            return Err(SimError::invalid("nodes[0] must be ground 0"));
        }
        if self.nodes.len() > 128 || self.elements.len() > 1024 {
            return Err(SimError::new(
                ErrorCode::ResourceExhausted,
                "reference circuit limit exceeded",
            ));
        }
        let mut names = BTreeSet::new();
        for name in &self.nodes {
            if name.is_empty() || name.len() > 256 || !names.insert(name) {
                return Err(SimError::invalid(
                    "node names must be nonempty, unique and at most 256 bytes",
                ));
            }
        }
        let mut ids = BTreeSet::new();
        for e in &self.elements {
            if e.id().is_empty() || e.id().len() > 256 || !ids.insert(e.id()) {
                return Err(SimError::invalid(
                    "element IDs must be nonempty, unique and at most 256 bytes",
                ));
            }
            let (p, n) = e.terminals();
            if p == n || p.0 >= self.nodes.len() || n.0 >= self.nodes.len() {
                return Err(SimError::invalid(format!(
                    "invalid terminals on {}",
                    e.id()
                )));
            }
            if let Element::PspiceSwitch {
                cp,
                cn,
                ron,
                roff,
                von,
                voff,
                ..
            } = e
            {
                if cp.0 >= self.nodes.len() || cn.0 >= self.nodes.len() {
                    return Err(SimError::invalid("invalid switch control terminals"));
                }
                crate::pspice_switch::SwitchModel {
                    ron: *ron,
                    roff: *roff,
                    von: *von,
                    voff: *voff,
                }
                .validate()?;
            }
            if let Element::JunctionStorage{early_effect:Some(control),..}=e {
                if control.cp.0>=self.nodes.len() || control.cn.0>=self.nodes.len()
                    || !control.volts.is_finite() || control.volts<=0. || !control.volts.recip().is_finite() {
                    return Err(SimError::invalid("invalid forward Early charge control"));
                }
            }
            match e {
                Element::BehavioralVoltage {cp,cn,expression,..} | Element::BehavioralResistor {cp,cn,expression,..} => {
                    if cp.0>=self.nodes.len() || cn.0>=self.nodes.len() {
                        return Err(SimError::invalid("invalid behavioral control terminals"));
                    }
                    expression.validate()?;
                }
                Element::BsimSoiFdReference {
                    gate, back_gate, ..
                } if gate.0 >= self.nodes.len() || back_gate.0 >= self.nodes.len() => {
                    return Err(SimError::invalid("invalid SOI FD gate/back-gate terminal"));
                }
                Element::Bsim330 {
                    gate, bulk, model, ..
                } => {
                    if gate.0 >= self.nodes.len() || bulk.0 >= self.nodes.len() {
                        return Err(SimError::invalid("invalid BSIM3 gate/bulk terminal"));
                    }
                    model.validate()?;
                }
                Element::Mos1Channel {
                    gate, bulk, model, ..
                } => {
                    if gate.0 >= self.nodes.len() || bulk.0 >= self.nodes.len() {
                        return Err(SimError::invalid("invalid MOS1 gate/bulk terminal"));
                    }
                    model.validate()?;
                }
                Element::Vccs {
                    cp, cn, siemens, ..
                } if cp == cn
                    || cp.0 >= self.nodes.len()
                    || cn.0 >= self.nodes.len()
                    || !siemens.is_finite() =>
                {
                    return Err(SimError::invalid("invalid VCCS control terminals or gain"));
                }
                Element::Vcvs { cp, cn, gain, .. }
                    if cp == cn
                        || cp.0 >= self.nodes.len()
                        || cn.0 >= self.nodes.len()
                        || !gain.is_finite() =>
                {
                    return Err(SimError::invalid("invalid VCVS control terminals or gain"));
                }
                Element::VoltageScaledCccs {cp,cn,voltage_coefficient,..}
                    if cp.0>=self.nodes.len() || cn.0>=self.nodes.len() || !voltage_coefficient.is_finite() => {
                    return Err(SimError::invalid("invalid voltage-scaled CCCS voltage control"));
                }
                Element::Cccs {
                    control_id, gain, ..
                } | Element::VoltageScaledCccs {
                    control_id, gain, ..
                } if control_id.is_empty() || !gain.is_finite() => {
                    return Err(SimError::invalid("invalid CCCS control ID or gain"));
                }
                Element::Ccvs {
                    control_id, ohms, ..
                } if control_id.is_empty() || !ohms.is_finite() => {
                    return Err(SimError::invalid(
                        "invalid CCVS control ID or transresistance",
                    ));
                }
                Element::Resistor { ohms, .. } if !ohms.is_finite() || *ohms <= 0.0 => {
                    return Err(SimError::invalid("resistance must be finite and positive"));
                }
                Element::Capacitor { farads, .. } if !farads.is_finite() || *farads <= 0.0 => {
                    return Err(SimError::invalid("capacitance must be finite and positive"));
                }
                Element::Inductor { henries, .. } if !henries.is_finite() || *henries <= 0.0 => {
                    return Err(SimError::invalid("inductance must be finite and positive"));
                }
                Element::Voltage { volts, .. } if !volts.is_finite() => {
                    return Err(SimError::invalid("voltage must be finite"));
                }
                Element::Current { amps, .. } if !amps.is_finite() => {
                    return Err(SimError::invalid("current must be finite"));
                }
                Element::DiodeWithCapacitance { farads, .. }
                    if !farads.is_finite() || *farads <= 0. =>
                {
                    return Err(SimError::invalid(
                        "diode parallel capacitance must be finite and positive",
                    ));
                }
                Element::Diode {
                    saturation_amps,
                    thermal_volts,
                    ..
                }
                | Element::DiodeWithCapacitance {
                    saturation_amps,
                    thermal_volts,
                    ..
                } if !saturation_amps.is_finite()
                    || !thermal_volts.is_finite()
                    || *saturation_amps <= 0.
                    || *thermal_volts <= 0. =>
                {
                    return Err(SimError::invalid(
                        "diode parameters must be finite and positive",
                    ));
                }
                Element::SpiceDiode { model, .. } | Element::JunctionStorage { model, .. } => {
                    model.validate()?
                }
                _ => {}
            }
        }
        Ok(())
    }
}
