use crate::{
    error::{ErrorCode, SimError, SimResult},
    linalg::{BackendCapabilities, LinearSolver, Matrix},
    solver_contract::{SparseComplexSystem, SparseLinearBackend, SparseRealSystem},
    sparse::CscMatrix,
};
use faer::{
    prelude::*,
    sparse::{SparseColMat, Triplet},
};
/// Sparse factorization over the current bounded assembler, not a scalability claim.
#[derive(Debug, Default)]
pub struct FaerSparseLu;

/// Numerically empty rows/columns prove singularity despite retained symbolic
/// slots. This necessary support check does not establish general numerical rank.
fn require_numeric_support(
    n: usize,
    entries: impl Iterator<Item = (usize, usize, bool)>,
) -> SimResult<()> {
    let mut rows = vec![false; n];
    let mut columns = vec![false; n];
    for (row, column, nonzero) in entries {
        if nonzero {
            rows[row] = true;
            columns[column] = true;
        }
    }
    if rows.iter().chain(&columns).any(|present| !present) {
        return Err(SimError::new(
            ErrorCode::SingularMatrix,
            "numerically empty sparse row or column",
        ));
    }
    Ok(())
}

impl FaerSparseLu {
    /// Solves a compiler-produced canonical CSC plan without a dense conversion.
    /// Analysis drivers adopt this path only after their generic solver contract is widened.
    pub fn solve_compiled_csc(&self, matrix: &CscMatrix, rhs: &[f64]) -> SimResult<Vec<f64>> {
        let system = SparseRealSystem::new(matrix.pattern().clone(), matrix.values().to_vec())?;
        self.solve_sparse_real(&system, rhs)
    }
}

impl SparseLinearBackend for FaerSparseLu {
    fn sparse_capabilities(&self) -> BackendCapabilities {
        self.capabilities()
    }
    fn solve_sparse_real(&self, system: &SparseRealSystem, rhs: &[f64]) -> SimResult<Vec<f64>> {
        system.validate_rhs(rhs)?;
        require_numeric_support(
            system.pattern().dimension(),
            system
                .pattern()
                .slots()
                .zip(system.values())
                .map(|((row, column), value)| (row, column, *value != 0.)),
        )?;
        let entries: Vec<_> = system
            .pattern()
            .slots()
            .zip(system.values())
            .map(|((row, column), value)| Triplet::new(row, column, *value))
            .collect();
        let n = system.pattern().dimension();
        let sparse = SparseColMat::<usize, f64>::try_new_from_triplets(n, n, &entries)
            .map_err(|error| SimError::invalid(format!("sparse creation: {error:?}")))?;
        let lu = sparse.sp_lu().map_err(|error| {
            SimError::new(ErrorCode::SingularMatrix, format!("sparse LU: {error:?}"))
        })?;
        let solution = lu.solve(faer::Mat::from_fn(n, 1, |row, _| rhs[row]));
        let values: Vec<_> = (0..n).map(|row| solution[(row, 0)]).collect();
        if values.iter().any(|value| !value.is_finite()) {
            return Err(SimError::new(
                ErrorCode::NumericalFailure,
                "nonfinite sparse solution",
            ));
        }
        Ok(values)
    }
    fn solve_sparse_complex(
        &self,
        system: &SparseComplexSystem,
        rhs: &[f64],
    ) -> SimResult<Vec<num_complex::Complex64>> {
        system.validate_rhs(rhs)?;
        let complex: Vec<_> = rhs
            .iter()
            .map(|value| num_complex::Complex64::new(*value, 0.))
            .collect();
        self.solve_sparse_complex_rhs(system, &complex)
    }
    fn solve_sparse_complex_rhs(
        &self,
        system: &SparseComplexSystem,
        rhs: &[num_complex::Complex64],
    ) -> SimResult<Vec<num_complex::Complex64>> {
        system.validate_complex_rhs(rhs)?;
        require_numeric_support(
            system.pattern().dimension(),
            system
                .pattern()
                .slots()
                .zip(system.values())
                .map(|((row, column), value)| (row, column, value.re != 0. || value.im != 0.)),
        )?;
        let entries: Vec<_> = system
            .pattern()
            .slots()
            .zip(system.values())
            .map(|((row, column), value)| Triplet::new(row, column, *value))
            .collect();
        let n = system.pattern().dimension();
        let sparse =
            SparseColMat::<usize, num_complex::Complex64>::try_new_from_triplets(n, n, &entries)
                .map_err(|error| SimError::invalid(format!("sparse creation: {error:?}")))?;
        let lu = sparse.sp_lu().map_err(|error| {
            SimError::new(ErrorCode::SingularMatrix, format!("sparse LU: {error:?}"))
        })?;
        // One numeric factor handles both RHS components; no second
        // quadrature factorization or dense reference materialization.
        let solution = lu.solve(faer::Mat::from_fn(n, 1, |row, _| rhs[row]));
        let values: Vec<_> = (0..n).map(|row| solution[(row, 0)]).collect();
        if values
            .iter()
            .any(|value| !value.re.is_finite() || !value.im.is_finite())
        {
            return Err(SimError::new(
                ErrorCode::NumericalFailure,
                "nonfinite sparse complex solution",
            ));
        }
        Ok(values)
    }
}

impl LinearSolver for FaerSparseLu {
    fn prepare_csc(
        &self,
        matrix: &CscMatrix,
    ) -> SimResult<Option<crate::linalg::PreparedSparseReal>> {
        let n = matrix.dimension();
        require_numeric_support(
            n,
            matrix
                .entries()
                .map(|(row, column, value)| (row, column, value != 0.)),
        )?;
        let entries: Vec<_> = matrix
            .entries()
            .map(|(row, column, value)| Triplet::new(row, column, value))
            .collect();
        let sparse = SparseColMat::<usize, f64>::try_new_from_triplets(n, n, &entries)
            .map_err(|error| SimError::invalid(format!("{error:?}")))?;
        // The closure owns one numeric factor; changing RHS never refactors.
        let lu = sparse
            .sp_lu()
            .map_err(|error| SimError::new(ErrorCode::SingularMatrix, format!("{error:?}")))?;
        Ok(Some(crate::linalg::PreparedSparseReal::new(
            matrix.clone(),
            move |rhs| {
                let values = lu.solve(faer::Mat::from_fn(n, 1, |row, _| rhs[row]));
                Ok((0..n).map(|row| values[(row, 0)]).collect())
            },
        )))
    }
    fn solve_complex_rhs(
        &self,
        real: &Matrix,
        imag: &Matrix,
        rhs: &[num_complex::Complex64],
    ) -> SimResult<Vec<num_complex::Complex64>> {
        // Compatibility entry point for bounded dense reference callers. The
        // direct CSC entry above avoids both of these dense scans.
        let real = CscMatrix::from_dense(real)?;
        let imaginary = CscMatrix::from_dense(imag)?;
        let system = SparseComplexSystem::from_real_parts(&real, &imaginary, 1.)?;
        self.solve_sparse_complex_rhs(&system, rhs)
    }
    fn solve_csc(&self, matrix: &CscMatrix, rhs: &[f64]) -> SimResult<Vec<f64>> {
        self.solve_compiled_csc(matrix, rhs)
    }
    fn solve_complex_csc(
        &self,
        system: &SparseComplexSystem,
        rhs: &[num_complex::Complex64],
    ) -> SimResult<Vec<num_complex::Complex64>> {
        self.solve_sparse_complex_rhs(system, rhs)
    }
    fn prepare(&self, a: &Matrix) -> SimResult<Option<crate::linalg::PreparedReal>> {
        let Some(prepared) = self.prepare_csc(&CscMatrix::from_dense(a)?)? else {
            return Ok(None);
        };
        Ok(Some(crate::linalg::PreparedReal::new(
            a.clone(),
            move |rhs| prepared.solve(rhs),
        )?))
    }
    fn solve_complex(
        &self,
        real: &Matrix,
        imag: &Matrix,
        b: &[f64],
    ) -> SimResult<Vec<num_complex::Complex64>> {
        let rhs: Vec<_> = b
            .iter()
            .map(|value| num_complex::Complex64::new(*value, 0.))
            .collect();
        self.solve_complex_rhs(real, imag, &rhs)
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
        self.solve_compiled_csc(&CscMatrix::from_dense(a)?, b)
    }
}
