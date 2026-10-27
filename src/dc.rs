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
/// Prefer a compiler-owned sparse plan when the chosen provider implements it.
///
/// The dense matrix is retained solely as a bounded reference representation
/// and fallback for providers that explicitly decline canonical CSC. A sparse
/// provider failure is surfaced rather than silently changing algorithms.
pub fn solve_compiled_verified(
    plan: &CompiledCircuit,
    solver: &dyn LinearSolver,
) -> SimResult<(Vec<f64>, f64)> {
    solve_csc_verified(plan.conductance_sparse(), plan.rhs(), solver)
}

/// Solve canonical CSC with independent sparse residual validation. Only an
/// explicit Unsupported permits the same provider's bounded dense fallback.
pub fn solve_csc_verified(
    matrix: &crate::sparse::CscMatrix,
    rhs: &[f64],
    solver: &dyn LinearSolver,
) -> SimResult<(Vec<f64>, f64)> {
    validate_sparse_rhs(matrix, rhs)?;
    let values = match solver.solve_csc(matrix, rhs) {
        Ok(values) => values,
        Err(error) if error.code == ErrorCode::Unsupported => {
            solver.solve(&matrix.to_dense()?, rhs)?
        }
        Err(error) => return Err(error),
    };
    let error = verify_csc_solution(matrix, rhs, &values)?;
    Ok((values, error))
}

fn validate_sparse_rhs(matrix: &crate::sparse::CscMatrix, rhs: &[f64]) -> SimResult<()> {
    if rhs.len() != matrix.dimension() || rhs.iter().any(|value| !value.is_finite()) {
        return Err(SimError::invalid("invalid sparse real excitation"));
    }
    Ok(())
}

/// Componentwise backward error from CSC entries, independent of factorization.
/// A finite scale and 1e-10 acceptance are shared with the dense reference policy.
pub fn verify_csc_solution(
    matrix: &crate::sparse::CscMatrix,
    rhs: &[f64],
    values: &[f64],
) -> SimResult<f64> {
    validate_sparse_rhs(matrix, rhs)?;
    if values.len() != matrix.dimension() || values.iter().any(|value| !value.is_finite()) {
        return Err(SimError::new(
            ErrorCode::NumericalFailure,
            "invalid sparse real solution",
        ));
    }
    let mut sums = vec![0.; matrix.dimension()];
    let mut scales: Vec<_> = rhs.iter().map(|value| value.abs()).collect();
    for (row, column, coefficient) in matrix.entries() {
        let product = coefficient * values[column];
        sums[row] += product;
        scales[row] += product.abs();
    }
    let mut worst = 0_f64;
    for row in 0..matrix.dimension() {
        let error = (sums[row] - rhs[row]).abs() / scales[row].max(f64::MIN_POSITIVE);
        if !scales[row].is_finite() || !error.is_finite() || error > 1e-10 {
            return Err(SimError::new(
                ErrorCode::NumericalFailure,
                "sparse real residual failed",
            ));
        }
        worst = worst.max(error);
    }
    Ok(worst)
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
    operating_point_with_options(plan, solver, crate::newton::NewtonOptions::default())
}
/// Solve with a caller-selected bounded Newton policy.
pub fn operating_point_with_options(
    plan: &CompiledCircuit,
    solver: &dyn LinearSolver,
    options: crate::newton::NewtonOptions,
) -> SimResult<(Vec<f64>, Option<crate::newton::NewtonReport>)> {
    if plan.has_nonlinear_devices() {
        let (values, report) = crate::newton::solve(plan, solver, options)?;
        return Ok((values, Some(report)));
    }
    Ok((solve_compiled_verified(plan, solver)?.0, None))
}
