use crate::{
    api::AcSourcePhasor,
    circuit::Element,
    compile::CompiledCircuit,
    error::{ErrorCode, SimError, SimResult},
    linalg::LinearSolver,
    quantity::positive,
};
use num_complex::Complex64;
use serde::Serialize;
#[derive(Debug, Clone, Serialize)]
pub struct ComplexValue {
    pub real: f64,
    pub imag: f64,
}
#[derive(Debug, Clone, Serialize)]
pub struct AcSample {
    pub frequency_hz: f64,
    pub values: Vec<ComplexValue>,
}
/// Real rhs values are small-signal source amplitudes in this reference profile.
pub fn ac(
    plan: &CompiledCircuit,
    frequencies: &[f64],
    solver: &dyn LinearSolver,
) -> SimResult<Vec<AcSample>> {
    ac_with_source_phasors(plan, frequencies, &[], solver)
}

/// Solve a small-signal AC grid with optional complex independent-source values.
pub fn ac_with_source_phasors(
    plan: &CompiledCircuit,
    frequencies: &[f64],
    source_phasors: &[AcSourcePhasor],
    solver: &dyn LinearSolver,
) -> SimResult<Vec<AcSample>> {
    if frequencies.is_empty() || frequencies.len() > 10_000 {
        return Err(SimError::invalid("AC grid must contain 1..10000 points"));
    }
    let n = plan.conductance.n;
    if source_phasors.len() > 128 {
        return Err(SimError::invalid("AC source phasor budget exceeded"));
    }
    let mut rhs_real = plan.rhs.clone();
    let mut rhs_imag = vec![0.; n];
    let mut ids = std::collections::BTreeSet::new();
    for phasor in source_phasors {
        if phasor.id.is_empty() || !phasor.real.is_finite() || !phasor.imag.is_finite()
            || !ids.insert(&phasor.id) {
            return Err(SimError::invalid("invalid or duplicate AC source phasor"));
        }
        let element = plan.circuit.elements.iter().find(|element| element.id() == phasor.id)
            .ok_or_else(|| SimError::invalid("unknown AC source phasor"))?;
        match element {
            Element::Voltage { volts, .. } => {
                let row = plan.branch_index(&phasor.id)
                    .ok_or_else(|| SimError::invalid("missing AC voltage-source branch"))?;
                rhs_real[row] += phasor.real - *volts;
                rhs_imag[row] += phasor.imag;
            }
            Element::Current { p, n, amps, .. } => {
                let real_delta = phasor.real - *amps;
                if let Some(row) = p.unknown() {
                    rhs_real[row] -= real_delta;
                    rhs_imag[row] -= phasor.imag;
                }
                if let Some(row) = n.unknown() {
                    rhs_real[row] += real_delta;
                    rhs_imag[row] += phasor.imag;
                }
            }
            _ => return Err(SimError::invalid("AC phasor must target an independent source")),
        }
    }
    // Nonlinear small-signal AC is evaluated around the converged DC point. The
    // analytic Newton Jacobian contains the incremental diode conductances.
    let conductance = if plan.has_nonlinear_devices() {
        let bias = crate::dc::operating_point(plan, solver)?;
        crate::newton::linearize(plan, &bias)?.jacobian
    } else {
        plan.conductance.clone()
    };
    let mut output = Vec::with_capacity(frequencies.len());
    if n * frequencies.len() > 1_000_000 {
        return Err(SimError::new(
            ErrorCode::ResourceExhausted,
            "AC retained scalar budget exceeded",
        ));
    }
    for &frequency in frequencies {
        positive(frequency, "frequency_hz")?;
        let omega = std::f64::consts::TAU * frequency;
        let mut imag = plan.charge.clone();
        for value in &mut imag.values {
            *value *= omega;
        }
        let mut x = solver.solve_complex(&conductance, &imag, &rhs_real)?;
        if rhs_imag.iter().any(|value| *value != 0.) {
            let quadrature = solver.solve_complex(&conductance, &imag, &rhs_imag)?;
            for (value, quadrature_value) in x.iter_mut().zip(quadrature) {
                *value += Complex64::new(-quadrature_value.im, quadrature_value.re);
            }
        }
        if x.len() != n || x.iter().any(|v| !v.re.is_finite() || !v.im.is_finite()) {
            return Err(SimError::new(
                ErrorCode::NumericalFailure,
                "invalid complex solution",
            ));
        }
        for i in 0..n {
            let residual = (0..n)
                .map(|j| Complex64::new(conductance.at(i, j), imag.at(i, j)) * x[j])
                .sum::<Complex64>()
                - Complex64::new(rhs_real[i], rhs_imag[i]);
            let scale = Complex64::new(rhs_real[i], rhs_imag[i]).norm()
                + (0..n)
                    .map(|j| {
                        (Complex64::new(conductance.at(i, j), imag.at(i, j)) * x[j]).norm()
                    })
                    .sum::<f64>();
            let relative = residual.norm() / scale.max(f64::MIN_POSITIVE);
            if !scale.is_finite() || !relative.is_finite() || relative > 1e-10 {
                return Err(SimError::new(
                    ErrorCode::NumericalFailure,
                    "AC residual failed",
                ));
            }
        }
        output.push(AcSample {
            frequency_hz: frequency,
            values: x
                .iter()
                .map(|v| ComplexValue {
                    real: v.re,
                    imag: v.im,
                })
                .collect(),
        });
    }
    Ok(output)
}
