use crate::{compile::CompiledCircuit,configured::Method,dc::{solve_verified,verify_solution},error::{SimError,SimResult},linalg::{LinearSolver,PreparedReal}};
use std::collections::VecDeque;
pub(crate) struct RhsPair<'a>{pub previous:&'a[f64],pub current:&'a[f64]}
/// Cache scope is one immutable plan/method; keys are exact step-size bits.
pub(crate) struct Stepper<'a>{
    plan:&'a CompiledCircuit,solver:&'a dyn LinearSolver,method:Method,
    cache:VecDeque<(u64,PreparedReal)>,pub prepared_factors:usize,pub prepared_reuses:usize,pub solve_calls:usize
}
impl<'a> Stepper<'a>{
    pub fn new(plan:&'a CompiledCircuit,solver:&'a dyn LinearSolver,method:Method)->Self{Self{plan,solver,method,cache:VecDeque::new(),prepared_factors:0,prepared_reuses:0,solve_calls:0}}
    pub fn advance(&mut self,old:&[f64],h:f64,rhs:RhsPair<'_>)->SimResult<Vec<f64>>{
        let plan=self.plan;let method=self.method;
        if !h.is_finite()||h<=0.||old.len()!=plan.unknown_count(){return Err(SimError::invalid("invalid integration step/state"));}
        let alpha=match method{Method::BackwardEuler=>1./h,Method::Trapezoidal=>2./h};
        let mut matrix=plan.conductance.clone();let mut b=rhs.current.to_vec();
        for(i,bi)in b.iter_mut().enumerate(){
            let algebraic=(0..matrix.n).all(|j|plan.charge.at(i,j)==0.);
            for(j,&old_value)in old.iter().enumerate(){
                let cq=alpha*plan.charge.at(i,j);matrix.add(i,j,cq);*bi+=cq*old_value;
                if method==Method::Trapezoidal&&!algebraic{*bi-=plan.conductance.at(i,j)*old_value;}
            }
            if algebraic{*bi=rhs.current[i];}else if method==Method::Trapezoidal{*bi+=rhs.previous[i];}
        }
        self.solve_calls+=1;
        let key=h.to_bits();
        if let Some(index)=self.cache.iter().position(|(k,_)|*k==key){
            self.prepared_reuses+=1;let x=self.cache[index].1.solve(&b)?;verify_solution(&matrix,&b,&x)?;return Ok(x);
        }
        if let Some(prepared)=self.solver.prepare(&matrix)?{
            self.prepared_factors+=1;let x=prepared.solve(&b)?;verify_solution(&matrix,&b,&x)?;
            self.cache.push_front((key,prepared));if self.cache.len()>2{self.cache.pop_back();}
            Ok(x)
        }else{Ok(solve_verified(&matrix,&b,self.solver)?.0)}
    }
}

