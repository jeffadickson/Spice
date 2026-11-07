use crate::{
    error::{ErrorCode, SimError, SimResult},
    linalg::{LinearSolver, Matrix},
    solver_contract::{SparseComplexSystem, SparseLinearBackend, SparseRealSystem},
};
use nalgebra::{DMatrix, DVector, Dyn, linalg::LU};
/// Dense diagnostic adapter; it is not a production sparse backend.
#[derive(Debug, Default)]
pub struct DenseLu;

/// Converts the neutral CSC contract only inside the legacy dense adapter.
fn real_dense(system: &SparseRealSystem) -> Matrix {
    let n = system.pattern().dimension();
    let mut values = vec![0.0; n * n];
    for ((row, column), value) in system.pattern().slots().zip(system.values()) {
        values[row * n + column] = *value;
    }
    Matrix { n, values }
}

/// Converts a neutral complex CSC contract only inside the legacy dense adapter.
fn complex_dense(system: &SparseComplexSystem) -> (Matrix, Matrix) {
    let n = system.pattern().dimension();
    let mut real = vec![0.0; n * n];
    let mut imaginary = vec![0.0; n * n];
    for ((row, column), value) in system.pattern().slots().zip(system.values()) {
        real[row * n + column] = value.re;
        imaginary[row * n + column] = value.im;
    }
    (
        Matrix { n, values: real },
        Matrix {
            n,
            values: imaginary,
        },
    )
}

impl SparseLinearBackend for DenseLu {
    fn sparse_capabilities(&self) -> crate::linalg::BackendCapabilities {
        self.capabilities()
    }
    fn solve_sparse_real(&self, system: &SparseRealSystem, rhs: &[f64]) -> SimResult<Vec<f64>> {
        system.validate_rhs(rhs)?;
        LinearSolver::solve(self, &real_dense(system), rhs)
    }
    fn solve_sparse_complex(
        &self,
        system: &SparseComplexSystem,
        rhs: &[f64],
    ) -> SimResult<Vec<num_complex::Complex64>> {
        system.validate_rhs(rhs)?;
        let (real, imaginary) = complex_dense(system);
        LinearSolver::solve_complex(self, &real, &imaginary, rhs)
    }
    fn solve_sparse_complex_rhs(
        &self,
        system: &SparseComplexSystem,
        rhs: &[num_complex::Complex64],
    ) -> SimResult<Vec<num_complex::Complex64>> {
        system.validate_complex_rhs(rhs)?;
        let (real, imaginary) = complex_dense(system);
        LinearSolver::solve_complex_rhs(self, &real, &imaginary, rhs)
    }
}

impl LinearSolver for DenseLu {
    fn prepare(&self, a: &Matrix) -> SimResult<Option<crate::linalg::PreparedReal>> {
        a.validate(&vec![0.0; a.n.min(256)])?;
        let matrix = DMatrix::from_row_slice(a.n, a.n, &a.values);
        let lu = matrix.clone().lu();
        if !lu.is_invertible() {
            return Err(SimError::new(
                ErrorCode::SingularMatrix,
                "singular prepared LU",
            ));
        }
        Ok(Some(crate::linalg::PreparedReal::new(
            a.clone(),
            move |b| solve_refined_real_lu(&matrix, &lu, &DVector::from_column_slice(b)),
        )?))
    }
    fn capabilities(&self) -> crate::linalg::BackendCapabilities {
        crate::linalg::BackendCapabilities {
            id: self.name(),
            placement: "cpu",
            max_unknowns: 256,
            sparse: false,
            gpu: false,
            complex: true,
            prepared_real: true,
            provider_version: "nalgebra 0.34.1",
        }
    }
    fn name(&self) -> &'static str {
        "nalgebra_dense_lu_reference"
    }
    fn solve_complex(
        &self,
        real: &Matrix,
        imag: &Matrix,
        rhs: &[f64],
    ) -> SimResult<Vec<num_complex::Complex64>> {
        let complex: Vec<_> = rhs
            .iter()
            .map(|v| num_complex::Complex64::new(*v, 0.))
            .collect();
        self.solve_complex_rhs(real, imag, &complex)
    }
    fn solve_complex_rhs(
        &self,
        real: &Matrix,
        imag: &Matrix,
        rhs: &[num_complex::Complex64],
    ) -> SimResult<Vec<num_complex::Complex64>> {
        real.validate(&vec![0.; real.n.min(256)])?;
        imag.validate(&vec![0.; imag.n.min(256)])?;
        if real.n != imag.n {
            return Err(SimError::invalid("complex matrix dimensions differ"));
        }
        if rhs.len() != real.n || rhs.iter().any(|v| !v.re.is_finite() || !v.im.is_finite()) {
            return Err(SimError::invalid("invalid complex right-hand side"));
        }
        let values: Vec<_> = real
            .values
            .iter()
            .zip(&imag.values)
            .map(|(r, i)| num_complex::Complex64::new(*r, *i))
            .collect();
        let a = DMatrix::from_row_slice(real.n, real.n, &values);
        let b = DVector::from_column_slice(rhs);
        let x = a
            .lu()
            .solve(&b)
            .ok_or_else(|| SimError::new(ErrorCode::SingularMatrix, "singular complex matrix"))?;
        if x.iter().any(|v| !v.re.is_finite() || !v.im.is_finite()) {
            return Err(SimError::new(
                ErrorCode::NumericalFailure,
                "nonfinite complex solution",
            ));
        }
        Ok(x.as_slice().to_vec())
    }
    fn solve(&self, a: &Matrix, b: &[f64]) -> SimResult<Vec<f64>> {
        a.validate(b)?;
        let matrix = DMatrix::from_row_slice(a.n, a.n, &a.values);
        let lu = matrix.clone().lu();
        solve_refined_real_lu(&matrix, &lu, &DVector::from_column_slice(b))
    }
}

/// Reuse this LU factor for at most three same-precision corrections. In MNA a
/// zero-valued source constraint can acquire tiny roundoff: the strict
/// componentwise error is then one even when the absolute voltage is tiny.
/// Correct the actual solution rather than weakening that acceptance policy.
/// These are factor-internal correction solves, not new logical analysis steps.
fn solve_refined_real_lu(
    matrix: &DMatrix<f64>,
    factor: &LU<f64, Dyn, Dyn>,
    rhs: &DVector<f64>,
) -> SimResult<Vec<f64>> {
    let mut values = factor
        .solve(rhs)
        .ok_or_else(|| SimError::new(ErrorCode::SingularMatrix, "LU has no unique solution"))?;
    if values.iter().any(|value| !value.is_finite()) {
        return Err(SimError::new(
            ErrorCode::NumericalFailure,
            "nonfinite LU solution",
        ));
    }
    enforce_homogeneous_singletons(matrix, rhs, &mut values);
    let mut best_error = real_backward_error(matrix, rhs, &values);
    for _ in 0..3 {
        if best_error <= 1e-10 {
            return Ok(values.as_slice().to_vec());
        }
        if !best_error.is_finite() {
            break;
        }
        let residual = rhs - matrix * &values;
        let correction = factor.solve(&residual).ok_or_else(|| {
            SimError::new(
                ErrorCode::SingularMatrix,
                "LU refinement has no unique solution",
            )
        })?;
        let mut candidate = &values + correction;
        if candidate.iter().any(|value| !value.is_finite()) {
            break;
        }
        enforce_homogeneous_singletons(matrix, rhs, &mut candidate);
        let error = real_backward_error(matrix, rhs, &candidate);
        // Never replace a better finite solve with a worse/nonfinite correction.
        // Equal errors can still improve an exactly constrained zero component.
        if error > best_error || !error.is_finite() {
            break;
        }
        values = candidate;
        best_error = error;
    }
    if best_error <= 1e-10 {
        Ok(values.as_slice().to_vec())
    } else {
        Err(SimError::new(
            ErrorCode::NumericalFailure,
            "LU refinement did not meet componentwise backward error",
        ))
    }
}
/// Enforce exact equations a*x[j] = 0 before checking the complete original
/// system. Such rows occur for grounded zero-voltage sources. Refinement can
/// leave a nonzero subnormal correction whose relative row error remains one.
/// This changes the candidate solution, never the equations or acceptance bound.
fn enforce_homogeneous_singletons(
    matrix: &DMatrix<f64>,
    rhs: &DVector<f64>,
    values: &mut DVector<f64>,
) {
    for row in 0..matrix.nrows() {
        if rhs[row] != 0. {
            continue;
        }
        let mut nonzero = (0..matrix.ncols()).filter(|&column| matrix[(row, column)] != 0.);
        if let Some(column) = nonzero.next() {
            if nonzero.next().is_none() {
                values[column] = 0.;
            }
        }
    }
}
/// Independent algorithm within the same library, not a second qualified provider.
#[derive(Debug, Default)]
pub struct DenseQr;
impl LinearSolver for DenseQr {
    fn capabilities(&self) -> crate::linalg::BackendCapabilities {
        crate::linalg::BackendCapabilities {
            id: self.name(),
            placement: "cpu",
            max_unknowns: 256,
            sparse: false,
            gpu: false,
            complex: false,
            prepared_real: false,
            provider_version: "nalgebra 0.34.1",
        }
    }
    fn name(&self) -> &'static str {
        "nalgebra_dense_qr_reference"
    }
    fn solve(&self, a: &Matrix, b: &[f64]) -> SimResult<Vec<f64>> {
        a.validate(b)?;
        let matrix = DMatrix::from_row_slice(a.n, a.n, &a.values);
        let factor = matrix.clone().qr();
        let rhs = DVector::from_column_slice(b);
        let mut x = factor
            .solve(&rhs)
            .ok_or_else(|| SimError::new(ErrorCode::SingularMatrix, "QR has no unique solution"))?;
        // Reuse the independent QR factor for a bounded same-precision
        // iterative refinement. Mixed-unit/ill-conditioned MNA rows can lose
        // small voltages despite a small global residual; the outer componentwise
        // backward-error and physical-oracle checks still decide acceptance.
        let mut best_error = real_backward_error(&matrix, &rhs, &x);
        for _ in 0..3 {
            let residual = &rhs - &matrix * &x;
            if residual.iter().all(|value| *value == 0.) {
                break;
            }
            let correction = factor.solve(&residual).ok_or_else(|| {
                SimError::new(
                    ErrorCode::SingularMatrix,
                    "QR refinement has no unique solution",
                )
            })?;
            let candidate = &x + correction;
            if candidate.iter().any(|value| !value.is_finite()) {
                break;
            }
            let error = real_backward_error(&matrix, &rhs, &candidate);
            // Equal normalized errors may still improve tiny components enough
            // for a later correction; permit ties within the fixed three-step budget.
            if error > best_error {
                break;
            }
            best_error = error;
            x = candidate;
        }
        if x.iter().any(|v| !v.is_finite()) {
            return Err(SimError::new(
                ErrorCode::NumericalFailure,
                "nonfinite QR solution",
            ));
        }
        Ok(x.as_slice().to_vec())
    }
}

/// Refinement is accepted only when independent componentwise backward error
/// improves. Same-precision corrections may stagnate or worsen an earlier solve.
fn real_backward_error(matrix: &DMatrix<f64>, rhs: &DVector<f64>, values: &DVector<f64>) -> f64 {
    let mut worst = 0_f64;
    for row in 0..matrix.nrows() {
        let mut sum = 0.;
        let mut scale = rhs[row].abs();
        for column in 0..matrix.ncols() {
            let product = matrix[(row, column)] * values[column];
            sum += product;
            scale += product.abs();
        }
        let error = (sum - rhs[row]).abs() / scale.max(f64::MIN_POSITIVE);
        if !scale.is_finite() || !error.is_finite() {
            return f64::INFINITY;
        }
        worst = worst.max(error);
    }
    worst
}

#[cfg(test)]
mod october28_contracts {
    use super::*;
    #[test]
    fn real_backward_error_has_an_independently_known_nonnegative_scale() {
        // 2*x=3 at x=1 has residual 1 and componentwise scale 2+3=5.
        let matrix = DMatrix::from_element(1, 1, 2.);
        let rhs = DVector::from_element(1, 3.);
        assert!(
            (real_backward_error(&matrix, &rhs, &DVector::from_element(1, 1.)) - 0.2).abs() < 1e-15
        );
        assert_eq!(
            real_backward_error(&matrix, &rhs, &DVector::from_element(1, 1.5)),
            0.
        );
        assert!(
            real_backward_error(&matrix, &rhs, &DVector::from_element(1, f64::INFINITY))
                .is_infinite()
        );
    }
}

#[cfg(test)]
mod lu_refinement_contracts {
    use super::*;

    #[test]
    fn wrong_factors_cannot_be_accepted_by_the_refinement_budget() {
        let matrix = DMatrix::from_element(1, 1, 1.);
        let rhs = DVector::from_element(1, 1.);
        // These factors solve different equations: bounded corrections must not
        // relabel a persistently wrong solution as an accepted one.
        for wrong_coefficient in [2., -1., 0.5, 1e-308] {
            let factor = DMatrix::from_element(1, 1, wrong_coefficient).lu();
            assert_eq!(
                solve_refined_real_lu(&matrix, &factor, &rhs)
                    .unwrap_err()
                    .code,
                ErrorCode::NumericalFailure
            );
        }
    }

    #[test]
    fn singular_factors_and_nonfinite_solutions_are_terminal() {
        let matrix = DMatrix::from_element(1, 1, 1.);
        let singular = DMatrix::zeros(1, 1).lu();
        assert_eq!(
            solve_refined_real_lu(&matrix, &singular, &DVector::from_element(1, 1.))
                .unwrap_err()
                .code,
            ErrorCode::SingularMatrix
        );
        let tiny = DMatrix::from_element(1, 1, 1e-308);
        assert_eq!(
            solve_refined_real_lu(
                &tiny,
                &tiny.clone().lu(),
                &DVector::from_element(1, f64::MAX)
            )
            .unwrap_err()
            .code,
            ErrorCode::NumericalFailure
        );
    }

    #[test]
    fn zero_constraint_roundoff_is_still_rejected_independently() {
        let matrix = DMatrix::from_element(1, 1, 1.);
        let rhs = DVector::zeros(1);
        assert_eq!(
            real_backward_error(&matrix, &rhs, &DVector::from_element(1, f64::EPSILON)),
            1.
        );
        assert_eq!(
            solve_refined_real_lu(&matrix, &matrix.clone().lu(), &rhs).unwrap(),
            vec![0.]
        );
        assert!(
            DenseLu
                .solve(
                    &Matrix {
                        n: 1,
                        values: vec![f64::NAN]
                    },
                    &[1.]
                )
                .is_err()
        );
        assert!(
            DenseLu
                .solve(
                    &Matrix {
                        n: 1,
                        values: vec![1.]
                    },
                    &[f64::INFINITY]
                )
                .is_err()
        );
    }
}
