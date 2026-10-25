use crate::{
    compile::CompiledCircuit,
    error::{ErrorCode, SimError, SimResult},
    linalg::{LinearSolver, Matrix},
};
/// Backward error is checked independently; a vendor success status is insufficient.
pub fn solve_verified(
    a: &Matrix,
    b: &[f64],
    solver: &dyn LinearSolver,
) -> SimResult<(Vec<f64>, f64)> {
    a.validate(b)?;
    let x = solver.solve(a, b)?;
    let error = verify_solution(a, b, &x)?;
    Ok((x, error))
}
/// Numerical validation remains outside the backend, including cached-factor solves.
pub fn verify_solution(a: &Matrix, b: &[f64], x: &[f64]) -> SimResult<f64> {
    a.validate(b)?;
    if x.len() != a.n || x.iter().any(|v| !v.is_finite()) {
        return Err(SimError::new(
            ErrorCode::NumericalFailure,
            "backend returned invalid solution",
        ));
    }
    let mut worst = 0.0_f64;
    for (i, &rhs_value) in b.iter().enumerate() {
        let sum: f64 = (0..a.n).map(|j| a.at(i, j) * x[j]).sum();
        let scale: f64 = rhs_value.abs() + (0..a.n).map(|j| (a.at(i, j) * x[j]).abs()).sum::<f64>();
        if !sum.is_finite() || !scale.is_finite() {
            return Err(SimError::new(
                ErrorCode::NumericalFailure,
                "residual accumulation overflow",
            ));
        }
        let error = (sum - rhs_value).abs() / scale.max(f64::MIN_POSITIVE);
        if !error.is_finite() {
            return Err(SimError::new(
                ErrorCode::NumericalFailure,
                "nonfinite residual",
            ));
        }
        worst = worst.max(error);
    }
    if worst > 1e-10 {
        return Err(SimError::new(
            ErrorCode::NumericalFailure,
            format!("backward error {worst} exceeds 1e-10"),
        ));
    }
    Ok(worst)
}
pub fn operating_point(plan: &CompiledCircuit, solver: &dyn LinearSolver) -> SimResult<Vec<f64>> {
    Ok(operating_point_detailed(plan, solver)?.0)
}

/// Solve an operating point and retain Newton evidence when nonlinear devices exist.
pub fn operating_point_detailed(
    plan: &CompiledCircuit,
    solver: &dyn LinearSolver,
) -> SimResult<(Vec<f64>, Option<crate::newton::NewtonReport>)> {
    if plan.has_nonlinear_devices() {
        let (values, report) = crate::newton::solve(plan, solver, crate::newton::NewtonOptions::default())?;
        return Ok((values, Some(report)));
    }
    Ok((solve_verified(&plan.conductance, &plan.rhs, solver)?.0, None))
}
