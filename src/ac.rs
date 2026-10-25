use crate::{
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
    if frequencies.is_empty() || frequencies.len() > 10_000 {
        return Err(SimError::invalid("AC grid must contain 1..10000 points"));
    }
    let n = plan.conductance.n;
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
        let x = solver.solve_complex(&conductance, &imag, &plan.rhs)?;
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
                - plan.rhs[i];
            let scale = plan.rhs[i].abs()
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
