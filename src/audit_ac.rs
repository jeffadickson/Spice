//! Linear peak-phasor equations reconstructed from elements, without MNA matrices.
use super::*;
use crate::{ac::AcSample, api::AcSourcePhasor};
use num_complex::Complex64 as C;
use std::collections::BTreeMap;

fn node(state: &[C], id: crate::ids::NodeId) -> C {
    id.unknown().map(|i| state[i]).unwrap_or_default()
}
fn branch(circuit: &Circuit, state: &[C], id: &str) -> SimResult<C> {
    let index = branch_ids(circuit)
        .iter()
        .position(|name| *name == id)
        .ok_or_else(|| SimError::invalid("audit missing complex branch"))?;
    Ok(state[circuit.nodes.len() - 1 + index])
}

/// Audit one linear AC state using peak phasors, passive p-to-n signs and SI
/// units. Nonlinear incremental bias is absent from the public result, so those
/// checks remain explicitly unavailable rather than rerunning a solver here.
pub fn ac(
    circuit: &Circuit,
    sample: &AcSample,
    phasors: &[AcSourcePhasor],
    options: AuditOptions,
) -> SimResult<AuditReport> {
    options.validate()?;
    circuit.validate()?;
    let omega = std::f64::consts::TAU * sample.frequency_hz;
    if !sample.frequency_hz.is_finite()
        || sample.frequency_hz <= 0.
        || !omega.is_finite()
        || sample.values.len() != circuit.nodes.len() - 1 + branch_ids(circuit).len()
        || sample
            .values
            .iter()
            .any(|v| !v.real.is_finite() || !v.imag.is_finite())
        || phasors.len() > 128
    {
        return Err(SimError::invalid(
            "invalid complex audit state/frequency/budget",
        ));
    }
    let mut overrides = BTreeMap::new();
    for p in phasors {
        if !p.real.is_finite()
            || !p.imag.is_finite()
            || !circuit.elements.iter().any(|e| {
                e.id() == p.id && matches!(e, Element::Voltage { .. } | Element::Current { .. })
            })
            || overrides
                .insert(p.id.as_str(), C::new(p.real, p.imag))
                .is_some()
        {
            return Err(SimError::invalid("invalid or duplicate audit AC source"));
        }
    }
    if circuit.elements.iter().any(|e| {
        matches!(
            e,
            Element::Diode { .. }
                | Element::SpiceDiode { .. }
                | Element::DiodeWithCapacitance { .. }
                | Element::PspiceSwitch { .. }
        )
    }) {
        return Ok(AuditReport {
            checks: vec![Check::not_checked(
                "independent_physical_audit",
                "",
                "nonlinear AC incremental bias is not retained in the public result",
            )],
        });
    }
    let state: Vec<_> = sample
        .values
        .iter()
        .map(|v| C::new(v.real, v.imag))
        .collect();
    let mut sums = vec![C::default(); circuit.nodes.len()];
    let mut scales = vec![0.; sums.len()];
    let mut power = C::default();
    let mut power_scale = 0.;
    let mut checks = Vec::new();
    for e in &circuit.elements {
        let (p, n) = e.terminals();
        let v = node(&state, p) - node(&state, n);
        let current = match e {
            Element::Resistor { ohms, .. } => v / ohms,
            Element::Capacitor { farads, .. } => C::new(0., omega * farads) * v,
            Element::Current { id, amps, .. } => overrides
                .get(id.as_str())
                .copied()
                .unwrap_or(C::new(*amps, 0.)),
            Element::Vccs {
                cp, cn, siemens, ..
            } => siemens * (node(&state, *cp) - node(&state, *cn)),
            Element::Cccs {
                control_id, gain, ..
            } => gain * branch(circuit, &state, control_id)?,
            _ => branch(circuit, &state, e.id())?,
        };
        let expected = match e {
            Element::Voltage { id, volts, .. } => Some(
                overrides
                    .get(id.as_str())
                    .copied()
                    .unwrap_or(C::new(*volts, 0.)),
            ),
            Element::Inductor { henries, .. } => Some(C::new(0., omega * henries) * current),
            Element::Vcvs { cp, cn, gain, .. } => {
                Some(gain * (node(&state, *cp) - node(&state, *cn)))
            }
            Element::Ccvs {
                control_id, ohms, ..
            } => Some(ohms * branch(circuit, &state, control_id)?),
            _ => None,
        };
        if let Some(expected) = expected {
            checks.push(Check::measured(
                format!("constraint:{}", e.id()),
                (v - expected).norm(),
                options.voltage_absolute_v + options.relative * (v.norm() + expected.norm()),
                "V",
            ));
        }
        sums[p.0] += current;
        sums[n.0] -= current;
        scales[p.0] += current.norm();
        scales[n.0] += current.norm();
        // Peak amplitudes require the half factor; conjugation preserves the
        // sign of reactive power (inductors positive, capacitors negative).
        let s = (v * 0.5) * current.conj();
        power += s;
        power_scale += s.norm();
    }
    for (i, sum) in sums.iter().enumerate() {
        checks.push(Check::measured(
            format!("kcl:{}", circuit.nodes[i]),
            sum.norm(),
            options.current_absolute_a + options.relative * scales[i],
            "A",
        ));
    }
    let bound = options.power_absolute_w + options.relative * power_scale;
    checks.push(Check::measured("ac_power_real", power.re, bound, "W"));
    checks.push(Check::measured("ac_power_imag", power.im, bound, "var"));
    Ok(AuditReport { checks })
}
