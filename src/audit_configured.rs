//! Fixed-step discrete identities; accepted adaptive half-step history is absent.
use super::*;
use crate::{
    configured::{Method, TransientConfig},
    transient::Sample,
};

fn effective(
    circuit: &Circuit,
    config: &TransientConfig,
    before: f64,
    after: f64,
) -> SimResult<Circuit> {
    if config.sources.len() > 128 {
        return Err(SimError::invalid("audit source budget exceeded"));
    }
    let mut result = circuit.clone();
    let mut selected = std::collections::BTreeSet::new();
    for source in &config.sources {
        if !selected.insert(source.id.as_str()) {
            return Err(SimError::invalid("duplicate audit source"));
        }
        source.waveform.validate()?;
        let a = source.waveform.at(before)?;
        let b = source.waveform.at(after)?;
        let value = if config.method == Method::Trapezoidal {
            0.5 * a + 0.5 * b
        } else {
            b
        };
        let element = result
            .elements
            .iter_mut()
            .find(|e| e.id() == source.id)
            .ok_or_else(|| SimError::invalid("unknown audit source"))?;
        match element {
            Element::Voltage { volts, .. } => *volts = value,
            Element::Current { amps, .. } => *amps = value,
            _ => {
                return Err(SimError::invalid(
                    "audit override must target an independent source",
                ));
            }
        }
    }
    Ok(result)
}

/// Audit one accepted configured interval. BE includes numerical dissipation;
/// linear trapezoidal uses midpoint voltage/current and exact quadratic storage
/// differences. Source overrides are sampled at the same endpoints as the
/// integrator, without reading its assembled equations or prepared factors.
pub fn configured_step(
    circuit: &Circuit,
    previous: &Sample,
    state: &Sample,
    config: &TransientConfig,
    options: AuditOptions,
) -> SimResult<AuditReport> {
    options.validate()?;
    config.validate()?;
    validate_state(circuit, &previous.values)?;
    validate_state(circuit, &state.values)?;
    if let Some(initial) = &config.initial_state {
        validate_state(circuit, initial)?;
    }
    let h = state.time_s - previous.time_s;
    if !previous.time_s.is_finite()
        || previous.time_s < 0.
        || !state.time_s.is_finite()
        || !h.is_finite()
        || h <= 0.
        || state.time_s > config.stop_s
    {
        return Err(SimError::invalid("invalid configured audit interval"));
    }
    let effective = effective(circuit, config, previous.time_s, state.time_s)?;
    if config.adaptive.is_some() {
        return Ok(AuditReport {
            checks: vec![Check::not_checked(
                "independent_physical_audit",
                "",
                "adaptive accepted midpoint/half-step history is not retained; endpoint energy alone is insufficient",
            )],
        });
    }
    if config.method == Method::BackwardEuler {
        return backward_euler_step(&effective, &previous.values, &state.values, h, options);
    }
    if circuit.elements.iter().any(|e| {
        matches!(
            e,
            Element::Bsim330 { .. }
                | Element::BsimSoiFdReference { .. }
                | Element::Mos1Channel { .. }
                | Element::Diode { .. }
                | Element::DiodeWithCapacitance { .. }
                | Element::SpiceDiode { .. }
                | Element::JunctionStorage { .. }
        )
    }) {
        return Ok(AuditReport {
            checks: vec![Check::not_checked(
                "independent_physical_audit",
                "",
                "nonlinear trapezoidal is outside the supported integrator profile",
            )],
        });
    }
    let mid: Vec<_> = previous
        .values
        .iter()
        .zip(&state.values)
        .map(|(a, b)| 0.5 * a + 0.5 * b)
        .collect();
    let mut currents = dc_currents(&effective, &mid)?;
    let mut checks = constraint_checks(&effective, &mid, options)?;
    let mut energy = 0.;
    let mut scale = 0.;
    for (e, current) in effective.elements.iter().zip(&mut currents) {
        let (p, n) = e.terminals();
        let v = voltage(&mid, p) - voltage(&mid, n);
        let delta = (voltage(&state.values, p) - voltage(&state.values, n))
            - (voltage(&previous.values, p) - voltage(&previous.values, n));
        let term = match e {
            Element::Capacitor { farads, .. } => {
                *current = farads * delta / h;
                farads * v * delta
            }
            Element::Inductor { henries, .. } => {
                let di = branch_current(&effective, &state.values, e.id())?
                    - branch_current(&effective, &previous.values, e.id())?;
                let expected = henries * di / h;
                checks.retain(|c| c.id != format!("constraint:{}", e.id()));
                checks.push(Check::measured(
                    format!("constraint:{}", e.id()),
                    v - expected,
                    options.voltage_absolute_v + options.relative * (v.abs() + expected.abs()),
                    "V",
                ));
                henries * *current * di
            }
            _ => h * v * *current,
        };
        energy += term;
        scale += term.abs();
    }
    checks.extend(kcl_checks(
        &effective, &mid, &currents, options, None, None,
    )?);
    checks.push(Check::measured(
        "trap_energy_balance",
        energy,
        options.energy_absolute_j + options.relative * scale,
        "J",
    ));
    Ok(AuditReport { checks })
}
