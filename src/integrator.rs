use crate::{compile::CompiledCircuit,configured::Method,dc::solve_verified,error::{SimError,SimResult},linalg::LinearSolver};
pub(crate) struct RhsPair<'a>{pub previous:&'a[f64],pub current:&'a[f64]}
/// Q contains charge and signed flux; do not assume it is positive definite.
pub(crate) fn advance(plan:&CompiledCircuit,solver:&dyn LinearSolver,old:&[f64],h:f64,rhs:RhsPair<'_>,method:Method)->SimResult<Vec<f64>>{
    if !h.is_finite()||h<=0.||old.len()!=plan.unknown_count(){return Err(SimError::invalid("invalid integration step/state"));}
    let alpha=match method{Method::BackwardEuler=>1./h,Method::Trapezoidal=>2./h};
    let mut matrix=plan.conductance.clone();let mut b=rhs.current.to_vec();
    for(i,bi)in b.iter_mut().enumerate(){
        let algebraic=(0..matrix.n).all(|j|plan.charge.at(i,j)==0.);
        for(j,&old_value)in old.iter().enumerate(){
            let cq=alpha*plan.charge.at(i,j);matrix.add(i,j,cq);
            *bi+=cq*old_value;
            if method==Method::Trapezoidal&&!algebraic{*bi-=plan.conductance.at(i,j)*old_value;}
        }
        if algebraic{*bi=rhs.current[i];}else if method==Method::Trapezoidal{*bi+=rhs.previous[i];}
    }
    Ok(solve_verified(&matrix,&b,solver)?.0)
}

