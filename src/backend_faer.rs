use crate::{
    error::{ErrorCode, SimError, SimResult},
    linalg::{BackendCapabilities, LinearSolver, Matrix},
    sparse::CscMatrix,
};
use faer::{
    prelude::*,
    sparse::{SparseColMat, Triplet},
};
/// Sparse factorization over the current bounded assembler, not a scalability claim.
#[derive(Debug, Default)]
pub struct FaerSparseLu;
impl LinearSolver for FaerSparseLu {
    fn prepare(&self, a: &Matrix) -> SimResult<Option<crate::linalg::PreparedReal>> {
        let csc = CscMatrix::from_dense(a)?;
        let entries: Vec<_> = csc
            .entries()
            .map(|(i, j, v)| Triplet::new(i, j, v))
            .collect();
        let sparse = SparseColMat::<usize, f64>::try_new_from_triplets(a.n, a.n, &entries)
            .map_err(|e| SimError::invalid(format!("{e:?}")))?;
        let lu = sparse
            .sp_lu()
            .map_err(|e| SimError::new(ErrorCode::SingularMatrix, format!("{e:?}")))?;
        let n = a.n;
        Ok(Some(crate::linalg::PreparedReal::new(
            a.clone(),
            move |b| {
                let x = lu.solve(faer::Mat::from_fn(n, 1, |i, _| b[i]));
                Ok((0..n).map(|i| x[(i, 0)]).collect())
            },
        )?))
    }
    fn solve_complex(
        &self,
        real: &Matrix,
        imag: &Matrix,
        b: &[f64],
    ) -> SimResult<Vec<num_complex::Complex64>> {
        real.validate(b)?;
        imag.validate(b)?;
        if real.n != imag.n {
            return Err(SimError::invalid("complex dimensions differ"));
        }
        let mut entries = Vec::new();
        for i in 0..real.n {
            for j in 0..real.n {
                let value = num_complex::Complex64::new(
                    real.values[i * real.n + j],
                    imag.values[i * real.n + j],
                );
                if value != num_complex::Complex64::new(0.0, 0.0) {
                    entries.push(Triplet::new(i, j, value));
                }
            }
        }
        let sparse = SparseColMat::<usize, num_complex::Complex64>::try_new_from_triplets(
            real.n, real.n, &entries,
        )
        .map_err(|e| SimError::invalid(format!("{e:?}")))?;
        let lu = sparse
            .sp_lu()
            .map_err(|e| SimError::new(ErrorCode::SingularMatrix, format!("{e:?}")))?;
        let x = lu.solve(faer::Mat::from_fn(real.n, 1, |i, _| {
            num_complex::Complex64::new(b[i], 0.0)
        }));
        let values: Vec<_> = (0..real.n).map(|i| x[(i, 0)]).collect();
        if values
            .iter()
            .any(|v| !v.re.is_finite() || !v.im.is_finite())
        {
            return Err(SimError::new(
                ErrorCode::NumericalFailure,
                "nonfinite sparse complex solution",
            ));
        }
        Ok(values)
    }
    fn name(&self) -> &'static str {
        "faer_sparse_lu"
    }
    fn capabilities(&self) -> BackendCapabilities {
        BackendCapabilities {
            id: self.name(),
            placement: "cpu",
            max_unknowns: 256,
            sparse: true,
            gpu: false,
            complex: true,
            prepared_real: true,
            provider_version: "faer 0.24.4",
        }
    }
    fn solve(&self, a: &Matrix, b: &[f64]) -> SimResult<Vec<f64>> {
        a.validate(b)?;
        let csc = CscMatrix::from_dense(a)?;
        let entries: Vec<_> = csc
            .entries()
            .map(|(i, j, v)| Triplet::new(i, j, v))
            .collect();
        let sparse = SparseColMat::<usize, f64>::try_new_from_triplets(a.n, a.n, &entries)
            .map_err(|e| SimError::invalid(format!("sparse creation: {e:?}")))?;
        let lu = sparse
            .sp_lu()
            .map_err(|e| SimError::new(ErrorCode::SingularMatrix, format!("sparse LU: {e:?}")))?;
        let rhs = faer::Mat::from_fn(a.n, 1, |i, _| b[i]);
        let x = lu.solve(&rhs);
        let values: Vec<_> = (0..a.n).map(|i| x[(i, 0)]).collect();
        if values.iter().any(|v| !v.is_finite()) {
            return Err(SimError::new(
                ErrorCode::NumericalFailure,
                "nonfinite sparse solution",
            ));
        }
        Ok(values)
    }
}
