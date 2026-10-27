use crate::{
    compile::CompiledCircuit,
    configured::Method,
    dc::{solve_csc_verified, verify_csc_solution},
    error::{ErrorCode, SimError, SimResult},
    linalg::{LinearSolver, PreparedReal, PreparedSparseReal},
};
use std::collections::VecDeque;
pub(crate) struct RhsPair<'a> {
    pub previous: &'a [f64],
    pub current: &'a [f64],
}
enum Factors {
    Sparse(PreparedSparseReal),
    Reference(PreparedReal),
}
impl Factors {
    fn solve(&self, rhs: &[f64]) -> SimResult<Vec<f64>> {
        match self {
            Self::Sparse(session) => session.solve(rhs),
            Self::Reference(session) => session.solve(rhs),
        }
    }
}
/// Cache scope is one immutable plan/method; keys are exact step-size bits.
/// Two owned numeric systems are retained; a changing excitation never changes factors.
pub(crate) struct Stepper<'a> {
    plan: &'a CompiledCircuit,
    solver: &'a dyn LinearSolver,
    method: Method,
    cache: VecDeque<(u64, Factors)>,
    pub prepared_factors: usize,
    pub prepared_reuses: usize,
    pub solve_calls: usize,
}
impl<'a> Stepper<'a> {
    pub fn new(plan: &'a CompiledCircuit, solver: &'a dyn LinearSolver, method: Method) -> Self {
        Self {
            plan,
            solver,
            method,
            cache: VecDeque::new(),
            prepared_factors: 0,
            prepared_reuses: 0,
            solve_calls: 0,
        }
    }
    pub fn advance(&mut self, old: &[f64], h: f64, rhs: RhsPair<'_>) -> SimResult<Vec<f64>> {
        let system = crate::linear_dynamic::assemble(
            self.plan,
            self.method,
            old,
            h,
            rhs.previous,
            rhs.current,
        )?;
        let matrix = &system.matrix;
        let b = &system.rhs;
        self.solve_calls += 1;
        let key = h.to_bits();
        if let Some(index) = self.cache.iter().position(|(k, _)| *k == key) {
            self.prepared_reuses += 1;
            let values = self.cache[index].1.solve(b)?;
            let backward_error = verify_csc_solution(matrix, b, &values)?;
            tracing::debug!(
                step_s = h,
                backward_error,
                prepared_reuses = self.prepared_reuses,
                "linear_transient_factor_reused"
            );
            return Ok(values);
        }
        let factors = match self.solver.prepare_csc(matrix) {
            Ok(Some(session)) => {
                if session.matrix().pattern() != matrix.pattern()
                    || session.matrix().values() != matrix.values()
                {
                    return Err(SimError::new(
                        ErrorCode::NumericalFailure,
                        "prepared CSC identity mismatch",
                    ));
                }
                Factors::Sparse(session)
            }
            Ok(None) => return Ok(solve_csc_verified(matrix, b, self.solver)?.0),
            Err(error) if error.code == ErrorCode::Unsupported => {
                // Preparation can be unavailable while direct uncached CSC is
                // supported. Only its explicit Unsupported permits dense reference work.
                match self.solver.solve_csc(matrix, b) {
                    Ok(values) => {
                        verify_csc_solution(matrix, b, &values)?;
                        return Ok(values);
                    }
                    Err(error) if error.code == ErrorCode::Unsupported => {}
                    Err(error) => return Err(error),
                }
                let dense = matrix.to_dense()?;
                let Some(session) = self.solver.prepare(&dense)? else {
                    let values = self.solver.solve(&dense, b)?;
                    verify_csc_solution(matrix, b, &values)?;
                    return Ok(values);
                };
                if session.matrix().n != dense.n || session.matrix().values != dense.values {
                    return Err(SimError::new(
                        ErrorCode::NumericalFailure,
                        "prepared reference identity mismatch",
                    ));
                }
                Factors::Reference(session)
            }
            Err(error) => return Err(error),
        };
        self.prepared_factors += 1;
        let values = factors.solve(b)?;
        let backward_error = verify_csc_solution(matrix, b, &values)?;
        self.cache.push_front((key, factors));
        if self.cache.len() > 2 {
            if let Some((evicted, _)) = self.cache.pop_back() {
                tracing::debug!(
                    step_s = f64::from_bits(evicted),
                    "linear_transient_factor_evicted"
                );
            }
        }
        tracing::debug!(
            step_s = h,
            backward_error,
            prepared_factors = self.prepared_factors,
            "linear_transient_factor_prepared"
        );
        Ok(values)
    }
}

#[cfg(test)]
mod cache_contracts {
    use super::*;

    #[test]
    fn bounded_cache_reuses_exact_keys_and_refactors_after_eviction() {
        let plan = crate::compile::compile(
            &crate::netlist::parse("title\nR1 out 0 2\nC1 out 0 3\n.end").unwrap(),
        )
        .unwrap();
        let mut stepper = Stepper::new(
            &plan,
            &crate::backend_faer::FaerSparseLu,
            Method::BackwardEuler,
        );
        let mut state = vec![1.];
        let mut expected = 1.;
        for h in [0.25, 0.5, 0.25, 1., 0.5, 0.25] {
            state = stepper
                .advance(
                    &state,
                    h,
                    RhsPair {
                        previous: &[0.],
                        current: &[0.],
                    },
                )
                .unwrap();
            expected /= 1. + h / 6.;
            assert!((state[0] - expected).abs() < 1e-12);
            assert!(stepper.cache.len() <= 2);
        }
        assert_eq!(stepper.prepared_factors, 4);
        assert_eq!(stepper.prepared_reuses, 2);
        assert_eq!(stepper.solve_calls, 6);
    }

    #[test]
    fn adjacent_step_values_are_distinct_cache_identities() {
        let plan = crate::compile::compile(
            &crate::netlist::parse("title\nR1 out 0 2\nC1 out 0 3\n.end").unwrap(),
        )
        .unwrap();
        let mut stepper = Stepper::new(
            &plan,
            &crate::backend_faer::FaerSparseLu,
            Method::BackwardEuler,
        );
        let adjacent = f64::from_bits(0.25_f64.to_bits() + 1);
        for h in [0.25, adjacent, 0.25, adjacent] {
            stepper
                .advance(
                    &[1.],
                    h,
                    RhsPair {
                        previous: &[0.],
                        current: &[0.],
                    },
                )
                .unwrap();
        }
        assert_eq!(stepper.prepared_factors, 2);
        assert_eq!(stepper.prepared_reuses, 2);
    }
}
