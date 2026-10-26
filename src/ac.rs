use crate::{
    api::AcSourcePhasor,
    circuit::Element,
    compile::CompiledCircuit,
    error::{ErrorCode, SimError, SimResult},
    linalg::LinearSolver,
    quantity::positive,
    solver_contract::SparseComplexSystem,
    sparse::CscMatrix,
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
        if phasor.id.is_empty()
            || !phasor.real.is_finite()
            || !phasor.imag.is_finite()
            || !ids.insert(&phasor.id)
        {
            return Err(SimError::invalid("invalid or duplicate AC source phasor"));
        }
        let element = plan
            .circuit
            .elements
            .iter()
            .find(|element| element.id() == phasor.id)
            .ok_or_else(|| SimError::invalid("unknown AC source phasor"))?;
        match element {
            Element::Voltage { volts, .. } => {
                let row = plan
                    .branch_index(&phasor.id)
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
            _ => {
                return Err(SimError::invalid(
                    "AC phasor must target an independent source",
                ));
            }
        }
    }
    // Nonlinear small-signal AC is evaluated around the converged DC point. The
    // analytic Newton Jacobian contains the incremental diode conductances.
    let nonlinear_reference = if plan.has_nonlinear_devices() {
        let bias = crate::dc::operating_point(plan, solver)?;
        Some(crate::newton::linearize(plan, &bias)?.jacobian)
    } else {
        None
    };
    // Linear plans stay sparse. Nonlinear bias conversion remains an explicit
    // bounded reference path until direct diode Jacobian stamps are qualified.
    let conductance_sparse = match &nonlinear_reference {
        Some(matrix) => CscMatrix::from_dense(matrix)?,
        None => plan.conductance_sparse().clone(),
    };
    let rhs: Vec<_> = rhs_real
        .iter()
        .zip(&rhs_imag)
        .map(|(re, im)| Complex64::new(*re, *im))
        .collect();
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
        let system =
            SparseComplexSystem::from_real_parts(&conductance_sparse, plan.charge_sparse(), omega)?;
        system.validate_complex_rhs(&rhs)?;
        let (x, direct) = match solver.solve_complex_csc(&system, &rhs) {
            Ok(values) => (values, true),
            Err(error) if error.code == ErrorCode::Unsupported => {
                let conductance = nonlinear_reference
                    .as_ref()
                    .unwrap_or(plan.conductance_reference());
                let mut imag = plan.charge.clone();
                for value in &mut imag.values {
                    *value *= omega;
                }
                (solver.solve_complex_rhs(conductance, &imag, &rhs)?, false)
            }
            Err(error) => return Err(error),
        };
        let backward_error = verify_complex_system(&system, &rhs, &x)?;
        tracing::debug!(
            frequency_hz = frequency,
            direct_sparse = direct,
            backward_error,
            "ac_point_completed"
        );
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

/// Independently check componentwise backward error by traversing canonical
/// sparse coefficients. Vendor success and vector finiteness alone are insufficient.
pub fn verify_complex_system(
    system: &SparseComplexSystem,
    rhs: &[Complex64],
    values: &[Complex64],
) -> SimResult<f64> {
    system.validate_complex_rhs(rhs)?;
    let n = system.pattern().dimension();
    crate::linalg::validate_complex_solution(n, values)?;
    let mut sums = vec![Complex64::new(0., 0.); n];
    let mut scales: Vec<_> = rhs.iter().map(|v| v.norm()).collect();
    for ((row, column), coefficient) in system.pattern().slots().zip(system.values()) {
        let term = *coefficient * values[column];
        sums[row] += term;
        scales[row] += term.norm();
    }
    let mut worst = 0_f64;
    for row in 0..n {
        let relative = (sums[row] - rhs[row]).norm() / scales[row].max(f64::MIN_POSITIVE);
        if !scales[row].is_finite() || !relative.is_finite() || relative > 1e-10 {
            return Err(SimError::new(
                ErrorCode::NumericalFailure,
                "AC residual failed",
            ));
        }
        worst = worst.max(relative);
    }
    Ok(worst)
}
