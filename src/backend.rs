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
        real.validate(rhs)?;
        imag.validate(rhs)?;
        if real.n != imag.n {
            return Err(SimError::invalid("complex matrix dimensions differ"));
        }
        let values: Vec<_> = real
            .values
            .iter()
            .zip(&imag.values)
            .map(|(r, i)| num_complex::Complex64::new(*r, *i))
            .collect();
        let a = DMatrix::from_row_slice(real.n, real.n, &values);
        let b = DVector::from_iterator(
            real.n,
            rhs.iter().map(|v| num_complex::Complex64::new(*v, 0.0)),
        );
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
