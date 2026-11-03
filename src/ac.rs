use crate::{
    api::AcSourcePhasor,
    circuit::Element,
    compile::CompiledCircuit,
    error::{ErrorCode, SimError, SimResult},
    linalg::LinearSolver,
    quantity::positive,
    solver_contract::SparseComplexSystem,
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
    // Validate the entire request before any DC bias or provider work.
    if n * frequencies.len() > 1_000_000 {
        return Err(SimError::new(
            ErrorCode::ResourceExhausted,
            "AC retained scalar budget exceeded",
        ));
    }
    for &frequency in frequencies {
        positive(frequency, "frequency_hz")?;
        if !(std::f64::consts::TAU * frequency).is_finite() {
            return Err(SimError::invalid("AC angular frequency overflow"));
        }
    }
    let mut overrides = std::collections::BTreeMap::new();
    for phasor in source_phasors {
        if phasor.id.is_empty()
            || !phasor.real.is_finite()
            || !phasor.imag.is_finite()
            || overrides
                .insert(phasor.id.as_str(), Complex64::new(phasor.real, phasor.imag))
                .is_some()
        {
            return Err(SimError::invalid("invalid or duplicate AC source phasor"));
        }
        if !plan.circuit.elements.iter().any(|element| {
            element.id() == phasor.id
                && matches!(element, Element::Voltage { .. } | Element::Current { .. })
        }) {
            return Err(SimError::invalid(
                "AC phasor must target an independent source",
            ));
        }
    }
    // Assemble from the selected amplitudes, never subtract the nominal value
    // from an already assembled RHS: that loses small overrides by cancellation.
    // The raw API retains its historical nominal-as-AC-amplitude convention.
    let mut rhs = vec![Complex64::new(0., 0.); n];
    for element in &plan.circuit.elements {
        match element {
            Element::Voltage { id, volts, .. } => {
                let row = plan
                    .branch_index(id)
                    .ok_or_else(|| SimError::invalid("missing AC voltage-source branch"))?;
                rhs[row] = overrides
                    .get(id.as_str())
                    .copied()
                    .unwrap_or(Complex64::new(*volts, 0.));
            }
            Element::Current { id, p, n, amps } => {
                let amplitude = overrides
                    .get(id.as_str())
                    .copied()
                    .unwrap_or(Complex64::new(*amps, 0.));
                if let Some(row) = p.unknown() {
                    rhs[row] -= amplitude;
                }
                if let Some(row) = n.unknown() {
                    rhs[row] += amplitude;
                }
            }
            _ => {}
        }
    }
    if rhs
        .iter()
        .any(|value| !value.re.is_finite() || !value.im.is_finite())
    {
        return Err(SimError::invalid("AC source assembly overflow"));
    }
    // DC convergence is still the bounded Newton/reference path; incremental
    // AC conductance stamps and every subsequent complex solve stay sparse.
    let (conductance_sparse, charge_sparse) = if plan.has_nonlinear_devices() {
        let bias = crate::dc::operating_point(plan, solver)?;
        (
            crate::newton::conductance_at_bias_csc(plan, &bias)?,
            crate::newton::charge_at_bias_csc(plan, &bias)?,
        )
    } else {
        (
            plan.conductance_sparse().clone(),
            plan.charge_sparse().clone(),
        )
    };
    let mut output = Vec::with_capacity(frequencies.len());
    for &frequency in frequencies {
        positive(frequency, "frequency_hz")?;
        let omega = std::f64::consts::TAU * frequency;
        let system =
            SparseComplexSystem::from_real_parts(&conductance_sparse, &charge_sparse, omega)?;
        system.validate_complex_rhs(&rhs)?;
        let (x, direct) = match solver.solve_complex_csc(&system, &rhs) {
            Ok(values) => (values, true),
            Err(error) if error.code == ErrorCode::Unsupported => {
                let conductance = conductance_sparse.to_dense()?;
                let mut imag = charge_sparse.to_dense()?;
                for value in &mut imag.values {
                    *value *= omega;
                }
                (solver.solve_complex_rhs(&conductance, &imag, &rhs)?, false)
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
