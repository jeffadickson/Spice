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
            verify_csc_solution(matrix, b, &values)?;
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
            self.cache.pop_back();
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
