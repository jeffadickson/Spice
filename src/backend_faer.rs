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

const MAX_COMPLEX_CORRECTIONS: usize = 3;
/// The sparse factor is reused for a bounded number of same-precision corrections.
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
    polish_homogeneous_rows(system,rhs,&mut values)?;
    let mut best_error = crate::solver_contract::complex_backward_error(system, rhs, &values)?;
    for attempt in 0..MAX_COMPLEX_CORRECTIONS {
        if best_error <= 1e-10 {
            return Ok(values);
        }
        let mut residual = rhs.to_vec();
        for ((row, column), coefficient) in system.pattern().slots().zip(system.values()) {
            residual[row] -= *coefficient * values[column];
        }
        let correction = solve_correction(&residual)?;
        crate::linalg::validate_complex_solution(system.pattern().dimension(), &correction)?;
        let mut candidate: Vec<_> = values
            .iter()
            .zip(correction)
            .map(|(value, delta)| *value + delta)
            .collect();
        polish_homogeneous_rows(system,rhs,&mut candidate)?;
        let error = match crate::solver_contract::complex_backward_error(system, rhs, &candidate) {
            Ok(error) => error,
            Err(_) => break,
        };
        tracing::debug!(attempt,error,previous_error=best_error,"complex_factor_refinement");
        if error > best_error {
            break;
        }
        values = candidate;
        best_error = error;
    }
    crate::solver_contract::verify_complex_solution(system, rhs, &values)?;
    Ok(values)
}

/// Re-evaluate a homogeneous row for its dominant unknown when every other
/// coefficient is at most one epsilon of that coefficient. Reverse-junction
/// current sensors can have unit branch coefficients beside 1e-200 conductance;
/// factor corrections then stagnate above the actual tiny current. ALL other
/// products are retained, never treated as zero. This is one bounded row sweep,
/// followed by unchanged independent verification of EVERY equation. For a
/// true singleton the same algebraic consequence sets its unknown to exact zero.
fn polish_homogeneous_rows(system:&SparseComplexSystem,rhs:&[num_complex::Complex64],values:&mut [num_complex::Complex64])->SimResult<()> {
    system.validate_complex_rhs(rhs)?;
    let n=system.pattern().dimension();
    crate::linalg::validate_complex_solution(n,values)?;
    let zero=num_complex::Complex64::new(0.,0.);
    let magnitude=|c:num_complex::Complex64|c.re.abs().max(c.im.abs());
    let mut dominant:Vec<Option<(usize,num_complex::Complex64)>>=vec![None;n];
    for ((row,column),coefficient) in system.pattern().slots().zip(system.values()) {
        if *coefficient!=zero && dominant[row].is_none_or(|(_,previous)|magnitude(*coefficient)>magnitude(previous)) {
            dominant[row]=Some((column,*coefficient));
        }
    }
    let mut other_max=vec![0_f64;n];
    for ((row,column),coefficient) in system.pattern().slots().zip(system.values()) {
        if dominant[row].is_some_and(|(target,_)|target!=column){other_max[row]=other_max[row].max(magnitude(*coefficient));}
    }
    let eligible:Vec<_>=(0..n).map(|row|rhs[row]==zero && dominant[row].is_some_and(|(_,a)|other_max[row]<=magnitude(a)*f64::EPSILON)).collect();
    let mut rest=vec![zero;n];
    for ((row,column),coefficient) in system.pattern().slots().zip(system.values()) {
        if eligible[row] && dominant[row].is_some_and(|(target,_)|target!=column){rest[row]+=*coefficient*values[column];}
    }
    for row in 0..n {
        if eligible[row] {
            if let Some((column,a))=dominant[row] {
                // Generic Complex division squares the divisor. Normalize it
                // first so a finite tiny/huge coefficient cannot create 0/0
                // or an infinite denominator. A singleton needs no division.
                let scale=magnitude(a);
                let corrected=if rest[row]==zero {zero}else{(-rest[row]/scale)/(a/scale)};
                if !corrected.re.is_finite() || !corrected.im.is_finite(){
                    tracing::debug!(row,column,"homogeneous_row_polish_unavailable");
                    continue; // Optional correction; unchanged full verification remains authoritative.
                }
                if values[column]!=corrected {
                    tracing::debug!(row,column,real=values[column].re,imag=values[column].im,
                        corrected_real=corrected.re,corrected_imag=corrected.im,"complex_homogeneous_row_polished");
                    values[column]=corrected;
                }
            }
        }
    }
    Ok(())
}

/// Bounded same-factor correction for small cancellation errors in stiff real
/// MNA systems. Every candidate must satisfy the original all-row 1e-10 check;
/// there is no refactor, provider fallback or tolerance relaxation.
fn refine_real_solution(matrix:&CscMatrix,rhs:&[f64],mut values:Vec<f64>,mut solve:impl FnMut(&[f64])->SimResult<Vec<f64>>)->SimResult<Vec<f64>> {
    for attempt in 0..=3 {
        if crate::solver_contract::verify_csc_solution(matrix,rhs,&values).is_ok(){return Ok(values);}        // Reuse the established homogeneous-row algebra, retaining every small
        // coefficient. This removes factor leakage in an exact zero constraint,
        // and the original all-row verifier remains authoritative afterwards.
        let complex=SparseComplexSystem::new(matrix.pattern().clone(),matrix.values().iter().map(|v|num_complex::Complex64::new(*v,0.)).collect())?;
        let complex_rhs:Vec<_>=rhs.iter().map(|v|num_complex::Complex64::new(*v,0.)).collect();
        let mut polished:Vec<_>=values.iter().map(|v|num_complex::Complex64::new(*v,0.)).collect();
        polish_homogeneous_rows(&complex,&complex_rhs,&mut polished)?;
        for(value,polished)in values.iter_mut().zip(polished){*value=polished.re;}
        if crate::solver_contract::verify_csc_solution(matrix,rhs,&values).is_ok(){return Ok(values);}
        if attempt==3{break;}
        let mut residual=rhs.to_vec();
        for(row,column,coefficient)in matrix.entries(){residual[row]-=coefficient*values[column];}
        if residual.iter().any(|x|!x.is_finite()){return Err(SimError::new(ErrorCode::NumericalFailure,"nonfinite real refinement residual"));}
        let correction=solve(&residual)?;
        if correction.len()!=values.len()||correction.iter().any(|x|!x.is_finite()){return Err(SimError::new(ErrorCode::NumericalFailure,"invalid real factor correction"));}
        for(value,delta)in values.iter_mut().zip(correction){*value+=delta;}
        tracing::debug!(attempt,"real_factor_refinement");
    }
    crate::solver_contract::verify_csc_solution(matrix,rhs,&values)?;Ok(values)
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
        let refinement_matrix=matrix.clone();
        Ok(Some(crate::linalg::PreparedSparseReal::new(
            matrix.clone(),
            move |rhs| {
                let values = lu.solve(faer::Mat::from_fn(n, 1, |row, _| rhs[row]));
                refine_real_solution(&refinement_matrix,rhs,(0..n).map(|row|values[(row,0)]).collect(),|part| {
                    let correction=lu.solve(faer::Mat::from_fn(n,1,|row,_|part[row]));
                    Ok((0..n).map(|row|correction[(row,0)]).collect())
                })
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
    #[test]
    fn coupled_complex_correction_uses_original_residual_and_adds_the_delta() {
        // A=[[2,j],[0,3]], x=[1+2j,3-j]. Quarter-valued initial errors
        // make every residual product exact in binary. The independently
        // expanded equations test the factor boundary, not its own verifier.
        let system = SparseComplexSystem::new(
            CscPattern::new(2, vec![0, 1, 3], vec![0, 0, 1]).unwrap(),
            vec![C::new(2., 0.), C::new(0., 1.), C::new(3., 0.)],
        ).unwrap();
        let rhs = [C::new(3., 7.), C::new(9., -3.)];
        let initial = vec![C::new(0.25, 0.5), C::new(1., -0.25)];
        let mut calls = 0;
        let result = refine_complex_solution(&system, &rhs, initial, |residual| {
            calls += 1;
            assert_eq!(residual, &[C::new(2.25, 5.), C::new(6., -2.25)]);
            Ok(vec![C::new(0.75, 1.5), C::new(2., -0.75)])
        }).unwrap();
        assert_eq!(calls, 1);
        assert_eq!(result, vec![C::new(1., 2.), C::new(3., -1.)]);
    }
    fn identity() -> SparseComplexSystem {
        SparseComplexSystem::new(
            CscPattern::new(1, vec![0, 1], vec![0]).unwrap(),
            vec![C::new(1., 0.)],
        )
        .unwrap()
    }
    #[test]
    fn zero_singleton_roundoff_is_corrected_without_weakening_other_rows() {
        // [[1,1],[0,1]], with a retained symbolic zero at (1,0).
        let system=SparseComplexSystem::new(CscPattern::new(2,vec![0,2,4],vec![0,1,0,1]).unwrap(),
            vec![C::new(1.,0.),C::new(0.,0.),C::new(1.,0.),C::new(1.,0.)]).unwrap();
        let rhs=[C::new(1.,0.),C::new(0.,0.)];
        let leaked=vec![C::new(1.,0.),C::new(1e-30,-1e-30)];
        assert_eq!(complex_backward_error(&system,&rhs,&leaked).unwrap(),1.);
        let result=refine_complex_solution(&system,&rhs,leaked,|_|panic!("exact zero consequence needs no factor correction")).unwrap();
        assert_eq!(result,vec![C::new(1.,0.),C::new(0.,0.)]);
        assert!(refine_complex_solution(&system,&rhs,vec![C::new(0.5,0.),C::new(1e-30,0.)],|_|Ok(vec![C::new(0.,0.);2])).is_err());
    }
    #[test]
    fn nonzero_rhs_and_multiple_numerical_entries_are_not_projected() {
        // x0=1, x0+x1=0 is not the pair of constraints x0=0,x1=0.
        let system=SparseComplexSystem::new(CscPattern::new(2,vec![0,2,3],vec![0,1,1]).unwrap(),
            vec![C::new(1.,0.),C::new(1.,0.),C::new(1.,0.)]).unwrap();
        let mut values=vec![C::new(1.,0.),C::new(-1.,0.)];
        polish_homogeneous_rows(&system,&[C::new(1.,0.),C::new(0.,0.)],&mut values).unwrap();
        assert_eq!(values,vec![C::new(1.,0.),C::new(-1.,0.)]);
        assert!(polish_homogeneous_rows(&system,&[],&mut values).is_err());
        assert!(polish_homogeneous_rows(&system,&[C::new(1.,0.),C::new(0.,0.)],&mut []).is_err());
    }
    #[test]
    fn tiny_nonzero_coefficients_remain_in_the_homogeneous_equation() {
        let system=SparseComplexSystem::new(CscPattern::new(2,vec![0,2,3],vec![0,1,1]).unwrap(),
            vec![C::new(1.,0.),C::new(1e-200,0.),C::new(1.,0.)]).unwrap();
        for input in [1.,1e200] {
            let rhs=[C::new(input,0.),C::new(0.,0.)];
            let mut values=vec![rhs[0],C::new(1e-35,0.)];
            polish_homogeneous_rows(&system,&rhs,&mut values).unwrap();
            assert_eq!(values[1],C::new(-1e-200*input,0.));
            assert!(crate::solver_contract::verify_complex_solution(&system,&rhs,&values).is_ok());
        }
    }
    #[test]
    fn homogeneous_polishing_preserves_extreme_coefficient_scales() {
        for scale in [1e-200,1.,1e200] {
            let singleton=SparseComplexSystem::new(CscPattern::new(1,vec![0,1],vec![0]).unwrap(),vec![C::new(scale,scale)]).unwrap();
            let mut values=vec![C::new(1e-30,0.)];
            polish_homogeneous_rows(&singleton,&[C::new(0.,0.)],&mut values).unwrap();
            assert_eq!(values[0],C::new(0.,0.));
            assert!(crate::solver_contract::verify_complex_solution(&singleton,&[C::new(0.,0.)],&values).is_ok());
            let system=SparseComplexSystem::new(CscPattern::new(2,vec![0,2,3],vec![0,1,1]).unwrap(),
                vec![C::new(1.,0.),C::new(scale*1e-20,scale*1e-20),C::new(scale,scale)]).unwrap();
            let rhs=[C::new(1.,0.),C::new(0.,0.)];let mut values=vec![rhs[0],C::new(0.,0.)];
            polish_homogeneous_rows(&system,&rhs,&mut values).unwrap();
            assert!((values[1].re+1e-20).abs()<1e-35);
            assert!(crate::solver_contract::verify_complex_solution(&system,&rhs,&values).is_ok());
        }
    }
    proptest::proptest! {
        #![proptest_config(proptest::test_runner::Config{cases:64,rng_seed:proptest::test_runner::RngSeed::Fixed(2026111308),..Default::default()})]
        #[test]
        fn homogeneous_row_solution_is_preserved_across_decimal_scales(exponent in -250i32..250,gap in 20i32..40,input in 0.01f64..100.) {
            let scale=10f64.powi(exponent);let ratio=10f64.powi(-gap);
            let system=SparseComplexSystem::new(CscPattern::new(2,vec![0,2,3],vec![0,1,1]).unwrap(),
                vec![C::new(1.,0.),C::new(scale*ratio,scale*ratio),C::new(scale,scale)]).unwrap();
            let rhs=[C::new(input,0.),C::new(0.,0.)];let mut values=vec![rhs[0],C::new(1e-5,1e-5)];
            polish_homogeneous_rows(&system,&rhs,&mut values).unwrap();
            proptest::prop_assert!((values[1].re/(-ratio*input)-1.).abs()<1e-12);
            proptest::prop_assert!(crate::solver_contract::verify_complex_solution(&system,&rhs,&values).is_ok());
        }
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
            assert!((1..=MAX_COMPLEX_CORRECTIONS).contains(&calls.get()));
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

#[cfg(test)]
mod real_refinement_contracts {
 use super::*;
 #[test]fn homogeneous_real_reconstruction_preserves_small_terms_and_other_equations(){
  let a=CscMatrix::new(2,vec![0,2,3],vec![0,1,1],vec![1.,1e-30,1.]).unwrap();let rhs=[1.,0.];
  let x=refine_real_solution(&a,&rhs,vec![1.,1e-20],|_|panic!("exact homogeneous consequence needs no correction")).unwrap();assert_eq!(x,vec![1.,-1e-30]);
  let mut calls=0;assert!(refine_real_solution(&a,&rhs,vec![0.5,1e-20],|_|{calls+=1;Ok(vec![0.,0.])}).is_err());assert_eq!(calls,3);
 }
 #[test]fn nonfinite_real_factor_corrections_are_not_accepted(){
  let a=CscMatrix::new(1,vec![0,1],vec![0],vec![1.]).unwrap();
  assert!(refine_real_solution(&a,&[1.],vec![2.],|_|Ok(vec![f64::NAN])).is_err());
  assert!(refine_real_solution(&a,&[1.],vec![2.],|_|Ok(vec![])).is_err());
 }
}
