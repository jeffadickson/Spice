use crate::{
    circuit::Element,
    compile::CompiledCircuit,
    error::{SimError, SimResult},
};
use serde::Serialize;
#[derive(Debug, Serialize)]
pub struct DevicePower {
    pub id: String,
    pub absorbed_w: f64,
}
#[derive(Debug, Serialize)]
pub struct PowerReport {
    pub devices: Vec<DevicePower>,
    pub sum_absorbed_w: f64,
    pub sum_absolute_w: f64,
}
/// Absorbed power uses passive sign convention; sources may supply negative power.
pub fn dc_power(plan: &CompiledCircuit, x: &[f64]) -> SimResult<PowerReport> {
    if x.len() != plan.unknown_count() || x.iter().any(|v| !v.is_finite()) {
        return Err(SimError::invalid("invalid DC power state"));
    }
    let voltage = |node: crate::ids::NodeId| node.unknown().map(|i| x[i]).unwrap_or(0.);
    let mut devices = Vec::new();
    let mut sum = 0.;
    let mut absolute = 0.;
    for element in &plan.circuit.elements {
        let (p, n) = element.terminals();
        let v = voltage(p) - voltage(n);
        if let Some((nodes, model)) = element.native_mos() {
            let bias = nodes.map(voltage);
            let absorbed_w: f64 = bias
                .into_iter()
                .zip(model.evaluate(bias)?.current)
                .map(|(v, i)| v * i)
                .sum();
            sum += absorbed_w;
            absolute += absorbed_w.abs();
            devices.push(DevicePower {
                id: element.id().into(),
                absorbed_w,
            });
            continue;
        }
        let current = match element {
            Element::Bsim330 { .. } | Element::BsimSoiFdReference { .. } => {
                unreachable!("four-terminal power handled above")
            }
            Element::Mos1Channel {
                p,
                n,
                gate,
                bulk,
                model,
                ..
            } => {
                model
                    .linearize([*p, *gate, *n, *bulk].map(voltage))?
                    .current_amps
            }
            Element::JunctionStorage { .. } => 0.,
            Element::SpiceDiode { model, .. } => model.linearize(v)?.current_amps,
            Element::PspiceSwitch {
                cp,
                cn,
                ron,
                roff,
                von,
                voff,
                ..
            } => {
                crate::pspice_switch::SwitchModel {
                    ron: *ron,
                    roff: *roff,
                    von: *von,
                    voff: *voff,
                }
                .linearize(v, voltage(*cp) - voltage(*cn))?
                .current
            }
            Element::BehavioralResistor {cp,cn,expression,..} =>
                crate::behavioral_resistor::linearize(expression,v,voltage(*cp)-voltage(*cn))?.current,
            Element::Resistor { ohms, .. } => v / ohms,
            Element::Current { amps, .. } => *amps,
            Element::Vccs {
                cp, cn, siemens, ..
            } => siemens * (voltage(*cp) - voltage(*cn)),
            Element::VoltageScaledCccs {cp,cn,control_id,gain,voltage_coefficient,factor_offset,..} => {
                let control=plan.branch_index(control_id).ok_or_else(||SimError::invalid("missing voltage-scaled current control"))?;
                crate::voltage_scaled_cccs::linearize_with_offset(*gain,*voltage_coefficient,*factor_offset,x[control],voltage(*cp)-voltage(*cn))?.current_amps
            }
            Element::Cccs {
                control_id, gain, ..
            } => {
                gain * x[plan
                    .branch_index(control_id)
                    .ok_or_else(|| SimError::invalid("missing CCCS control branch"))?]
            }
            Element::Capacitor { .. } => 0.,
            Element::Diode {
                saturation_amps,
                thermal_volts,
                ..
            }
            | Element::DiodeWithCapacitance {
                saturation_amps,
                thermal_volts,
                ..
            } => {
                crate::model::DiodeModel {
                    saturation_amps: *saturation_amps,
                    thermal_volts: *thermal_volts,
                }
                .linearize(v)?
                .current_amps
            }
            Element::Voltage { id, .. }
            | Element::Inductor { id, .. }
            | Element::Vcvs { id, .. }
            | Element::BehavioralVoltage { id, .. }
            | Element::Ccvs { id, .. } => {
                x[plan
                    .branch_index(id)
                    .ok_or_else(|| SimError::invalid("missing power branch"))?]
            }
        };
        let power = v * current;
        if !power.is_finite() {
            return Err(SimError::invalid("power overflow"));
        }
        sum += power;
        absolute += power.abs();
        devices.push(DevicePower {
            id: element.id().into(),
            absorbed_w: power,
        });
    }
    if !sum.is_finite() || !absolute.is_finite() {
        return Err(SimError::invalid("power accumulation overflow"));
    }
    Ok(PowerReport {
        devices,
        sum_absorbed_w: sum,
        sum_absolute_w: absolute,
    })
}
