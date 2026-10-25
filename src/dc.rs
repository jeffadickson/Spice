use crate::{compile::CompiledCircuit,error::SimResult,linalg::LinearSolver};
/// Ground is implicit; source branch current follows the source terminal order.
pub fn operating_point(plan:&CompiledCircuit,solver:&dyn LinearSolver)->SimResult<Vec<f64>>{solver.solve(&plan.conductance,&plan.rhs)}

