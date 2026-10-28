use crate::{
    error::{ErrorCode, SimError, SimResult},
    linalg::{LinearSolver, Matrix},
    solver_contract::{SparseComplexSystem, SparseLinearBackend, SparseRealSystem},
};
use nalgebra::{DMatrix, DVector};
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
        let lu = DMatrix::from_row_slice(a.n, a.n, &a.values).lu();
        if !lu.is_invertible() {
            return Err(SimError::new(
                ErrorCode::SingularMatrix,
                "singular prepared LU",
            ));
        }
        Ok(Some(crate::linalg::PreparedReal::new(
            a.clone(),
            move |b| {
                let x = lu.solve(&DVector::from_column_slice(b)).ok_or_else(|| {
                    SimError::new(ErrorCode::SingularMatrix, "prepared LU failure")
                })?;
                Ok(x.as_slice().to_vec())
            },
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
        let rhs = DVector::from_column_slice(b);
        let x = matrix
            .lu()
            .solve(&rhs)
            .ok_or_else(|| SimError::new(ErrorCode::SingularMatrix, "LU has no unique solution"))?;
        if x.iter().any(|v| !v.is_finite()) {
            return Err(SimError::new(
                ErrorCode::NumericalFailure,
                "nonfinite solution",
            ));
        }
        Ok(x.as_slice().to_vec())
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
        let mut best_error = qr_backward_error(&matrix, &rhs, &x);
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
            let error = qr_backward_error(&matrix, &rhs, &candidate);
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
fn qr_backward_error(matrix: &DMatrix<f64>, rhs: &DVector<f64>, values: &DVector<f64>) -> f64 {
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
    fn qr_backward_error_has_an_independently_known_nonnegative_scale() {
        // 2*x=3 at x=1 has residual 1 and componentwise scale 2+3=5.
        let matrix = DMatrix::from_element(1, 1, 2.);
        let rhs = DVector::from_element(1, 3.);
        assert!(
            (qr_backward_error(&matrix, &rhs, &DVector::from_element(1, 1.)) - 0.2).abs() < 1e-15
        );
        assert_eq!(
            qr_backward_error(&matrix, &rhs, &DVector::from_element(1, 1.5)),
            0.
        );
        assert!(
            qr_backward_error(&matrix, &rhs, &DVector::from_element(1, f64::INFINITY))
                .is_infinite()
        );
    }
}
