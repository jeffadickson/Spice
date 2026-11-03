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
        refine_complex_solution(system, rhs, values, |part| {
            let corrected = lu.solve(faer::Mat::from_fn(n, 1, |row, _| part[row]));
            Ok((0..n).map(|row| corrected[(row, 0)]).collect())
        })
    }
}

/// The sparse factor is reused for at most three same-precision corrections.
/// Very small branch currents can lose relative accuracy beside large currents;
/// correct their actual residuals rather than relaxing componentwise acceptance.
/// These are provider-internal factor solves, not new logical AC point solves.
fn refine_complex_solution(
    system: &SparseComplexSystem,
    rhs: &[num_complex::Complex64],
    mut values: Vec<num_complex::Complex64>,
    mut solve_correction: impl FnMut(
        &[num_complex::Complex64],
    ) -> SimResult<Vec<num_complex::Complex64>>,
) -> SimResult<Vec<num_complex::Complex64>> {
    let mut best_error = crate::solver_contract::complex_backward_error(system, rhs, &values)?;
    for _ in 0..3 {
        if best_error <= 1e-10 {
            return Ok(values);
        }
        let mut residual = rhs.to_vec();
        for ((row, column), coefficient) in system.pattern().slots().zip(system.values()) {
            residual[row] -= *coefficient * values[column];
        }
        let correction = solve_correction(&residual)?;
        crate::linalg::validate_complex_solution(system.pattern().dimension(), &correction)?;
        let candidate: Vec<_> = values
            .iter()
            .zip(correction)
            .map(|(value, delta)| *value + delta)
            .collect();
        let error = match crate::solver_contract::complex_backward_error(system, rhs, &candidate) {
            Ok(error) => error,
            Err(_) => break,
        };
        if error > best_error {
            break;
        }
        values = candidate;
        best_error = error;
    }
    crate::solver_contract::verify_complex_solution(system, rhs, &values)?;
    Ok(values)
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

#[cfg(test)]
mod complex_refinement_contracts {
    use super::*;
    use crate::{solver_contract::complex_backward_error, sparse::CscPattern};
    use num_complex::Complex64 as C;
    use std::cell::Cell;
    fn identity() -> SparseComplexSystem {
        SparseComplexSystem::new(
            CscPattern::new(1, vec![0, 1], vec![0]).unwrap(),
            vec![C::new(1., 0.)],
        )
        .unwrap()
    }
    #[test]
    fn wrong_complex_factors_cannot_bypass_independent_acceptance_or_the_budget() {
        let system = identity();
        let rhs = [C::new(1., 2.)];
        for coefficient in [0., 0.5, -1.] {
            let calls = Cell::new(0);
            let result = refine_complex_solution(&system, &rhs, vec![rhs[0] * 2.], |residual| {
                calls.set(calls.get() + 1);
                Ok(vec![residual[0] * coefficient])
            });
            assert_eq!(result.unwrap_err().code, ErrorCode::NumericalFailure);
            assert!((1..=3).contains(&calls.get()));
        }
        assert_eq!(
            complex_backward_error(&system, &[C::new(0., 0.)], &[C::new(0., f64::EPSILON)])
                .unwrap(),
            1.
        );
        assert!(
            crate::solver_contract::verify_complex_solution(
                &system,
                &[C::new(0., 0.)],
                &[C::new(0., f64::EPSILON)]
            )
            .is_err()
        );
    }
    #[test]
    fn malformed_corrections_and_callback_failures_are_terminal() {
        let system = identity();
        let rhs = [C::new(1., 1.)];
        for correction in [
            vec![],
            vec![C::new(0., 0.); 2],
            vec![C::new(f64::NAN, 0.)],
            vec![C::new(0., f64::INFINITY)],
        ] {
            assert!(
                refine_complex_solution(&system, &rhs, vec![rhs[0] * 2.], |_| Ok(
                    correction.clone()
                ))
                .is_err()
            );
        }
        let error = refine_complex_solution(&system, &rhs, vec![rhs[0] * 2.], |_| {
            Err(SimError::new(
                ErrorCode::NumericalFailure,
                "correction sentinel",
            ))
        })
        .unwrap_err();
        assert_eq!(error.message, "correction sentinel");
        assert!(
            refine_complex_solution(&system, &rhs, vec![C::new(f64::NAN, 0.)], |_| panic!(
                "invalid initial solution must not trigger correction"
            ))
            .is_err()
        );
    }
    #[test]
    fn accepted_complex_solutions_need_no_correction_and_zero_rows_stay_strict() {
        let system = identity();
        let rhs = [C::new(2., -3.)];
        let result = refine_complex_solution(&system, &rhs, rhs.to_vec(), |_| {
            panic!("accepted solution must not be corrected")
        })
        .unwrap();
        assert_eq!(result, rhs);
        assert_eq!(
            crate::solver_contract::verify_complex_solution(&system, &rhs, &result).unwrap(),
            0.
        );
        assert!(crate::solver_contract::verify_complex_solution(&system, &[], &result).is_err());
    }
}
