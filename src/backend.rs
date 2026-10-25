use crate::{
    error::{ErrorCode, SimError, SimResult},
    linalg::{LinearSolver, Matrix},
};
use nalgebra::{DMatrix, DVector};
/// Dense diagnostic adapter; it is not a production sparse backend.
#[derive(Debug, Default)]
pub struct DenseLu;
impl LinearSolver for DenseLu {
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
    fn name(&self) -> &'static str {
        "nalgebra_dense_qr_reference"
    }
    fn solve(&self, a: &Matrix, b: &[f64]) -> SimResult<Vec<f64>> {
        a.validate(b)?;
        let x = DMatrix::from_row_slice(a.n, a.n, &a.values)
            .qr()
            .solve(&DVector::from_column_slice(b))
            .ok_or_else(|| SimError::new(ErrorCode::SingularMatrix, "QR has no unique solution"))?;
        if x.iter().any(|v| !v.is_finite()) {
            return Err(SimError::new(
                ErrorCode::NumericalFailure,
                "nonfinite QR solution",
            ));
        }
        Ok(x.as_slice().to_vec())
    }
}
