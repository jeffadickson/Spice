use crate::{adaptive::AdaptiveOptions,compile::CompiledCircuit,error::{ErrorCode,SimError,SimResult},linalg::LinearSolver,quantity::positive,sources::{SourceOverride,BoundSources},transient::Sample};
use serde::{Deserialize,Serialize};
#[derive(Debug,Clone,Copy,PartialEq,Eq,Default,Serialize,Deserialize)]
#[serde(rename_all="snake_case")]
pub enum Method{#[default]BackwardEuler,Trapezoidal}
#[derive(Debug,Clone,Serialize,Deserialize)]
#[serde(deny_unknown_fields)]
pub struct TransientConfig{
    pub stop_s:f64,pub max_step_s:f64,pub max_steps:usize,
    #[serde(default)]pub method:Method,
    #[serde(default)]pub initial_state:Option<Vec<f64>>,
    #[serde(default)]pub sources:Vec<SourceOverride>,
    #[serde(default)]pub adaptive:Option<AdaptiveOptions>,
}
impl Default for TransientConfig{
    fn default()->Self{Self{stop_s:0.005,max_step_s:1e-5,max_steps:10_000,method:Method::BackwardEuler,initial_state:None,sources:Vec::new(),adaptive:None}}
}
impl TransientConfig{
    pub fn validate(&self)->SimResult<()>{
        positive(self.stop_s,"stop_s")?;positive(self.max_step_s,"max_step_s")?;
        if self.max_steps==0||self.max_steps>100_000{return Err(SimError::invalid("configured step budget must be 1..100000"));}
        if let Some(options)=&self.adaptive{options.validate(self.max_step_s)?;}
        Ok(())
    }
}
#[derive(Debug,Serialize)]
pub struct IntegrationReport{pub method:Method,pub initialization:&'static str,pub accepted_steps:usize,pub rejected_steps:usize,pub attempts:usize,pub max_accepted_error:f64}
pub struct ConfiguredResult{pub samples:Vec<Sample>,pub report:IntegrationReport}
fn initial(plan:&CompiledCircuit,b:&[f64],state:Option<&[f64]>,solver:&dyn LinearSolver)->SimResult<Vec<f64>>{
    let Some(x)=state else{return Ok(crate::dc::solve_verified(&plan.conductance,b,solver)?.0)};
    if x.len()!=plan.unknown_count()||x.iter().any(|v|!v.is_finite()){return Err(SimError::invalid("invalid initial state shape/values"));}
    for(i,&rhs)in b.iter().enumerate(){
        if(0..x.len()).all(|j|plan.charge.at(i,j)==0.){
            let residual=(0..x.len()).map(|j|plan.conductance.at(i,j)*x[j]).sum::<f64>()-rhs;
            if !residual.is_finite()||residual.abs()>1e-10*(1.+rhs.abs()){return Err(SimError::invalid(format!("initial algebraic constraint failed at row {i}")));}
        }
    }Ok(x.to_vec())
}
pub fn run(plan:&CompiledCircuit,solver:&dyn LinearSolver,options:&TransientConfig,cancel:&std::sync::atomic::AtomicBool)->SimResult<ConfiguredResult>{
    run_observed(plan,solver,options,cancel,|_|Ok(()))
}
/// Trusted Rust consumers can observe accepted samples, including before cancellation.
/// Callback failure aborts the run; it cannot turn partial data into a success result.
pub fn run_observed(plan:&CompiledCircuit,solver:&dyn LinearSolver,options:&TransientConfig,cancel:&std::sync::atomic::AtomicBool,mut observer:impl FnMut(&Sample)->SimResult<()>)->SimResult<ConfiguredResult>{
    options.validate()?;
    let sources=BoundSources::new(plan,&options.sources)?;let mut old_rhs=sources.rhs(0.)?;
    let mut state=initial(plan,&old_rhs,options.initial_state.as_deref(),solver)?;
    let mut samples=vec![Sample{time_s:0.,values:state.clone()}];
    observer(&samples[0])?;
    let mut stepper=crate::integrator::Stepper::new(plan,solver,options.method);
    let(mut time,mut proposed)=(0.,options.max_step_s);let(mut steps,mut rejected,mut attempts)=(0,0,0);let mut max_error=0_f64;
    while time<options.stop_s{
        if cancel.load(std::sync::atomic::Ordering::Relaxed){return Err(SimError::new(ErrorCode::Cancelled,"configured run cancelled"));}
        if steps>=options.max_steps||(samples.len()+1)*plan.unknown_count()>2_000_000{return Err(SimError::new(ErrorCode::ResourceExhausted,"configured run budget exhausted"));}
        if options.adaptive.as_ref().is_some_and(|a|attempts>=a.max_attempts){return Err(SimError::new(ErrorCode::ResourceExhausted,"adaptive attempt budget exhausted"));}
        let mut next=(time+proposed).min(options.stop_s);
        if let Some(event)=sources.next_breakpoint(time,next){next=event;}
        if next<=time{return Err(SimError::new(ErrorCode::ResourceExhausted,"timestep cannot advance"));}
        let h=next-time;let new_rhs=sources.rhs(next)?;attempts+=1;
        let coarse=stepper.advance(&state,h,crate::integrator::RhsPair{previous:&old_rhs,current:&new_rhs})?;
        let accepted=if let Some(adaptive)=&options.adaptive{
            let mid=time+h/2.;if mid<=time||mid>=next{return Err(SimError::new(ErrorCode::NumericalFailure,"adaptive midpoint cannot advance"));}
            let mid_rhs=sources.rhs(mid)?;
            let half=stepper.advance(&state,h/2.,crate::integrator::RhsPair{previous:&old_rhs,current:&mid_rhs})?;
            let fine=stepper.advance(&half,next-mid,crate::integrator::RhsPair{previous:&mid_rhs,current:&new_rhs})?;
            let order=if options.method==Method::BackwardEuler{1}else{2};
            let error=adaptive.error(plan,&coarse,&fine,order)?;
            if error>1.{
                rejected+=1;
                if h<=adaptive.min_step_s*(1.+1e-12){return Err(SimError::new(ErrorCode::NumericalFailure,"local error exceeds tolerance at minimum step"));}
                proposed=(h/2.).max(adaptive.min_step_s);continue;
            }
            max_error=max_error.max(error);
            let factor=if error==0.{2.}else{(0.9*error.powf(-1./(order+1)as f64)).clamp(0.5,2.)};
            proposed=(h*factor).clamp(adaptive.min_step_s,options.max_step_s);fine
        }else{coarse};
        // Only accepted states, RHS values and times become visible to future steps.
        state=accepted;old_rhs=new_rhs;time=next;steps+=1;samples.push(Sample{time_s:time,values:state.clone()});
        observer(samples.last().expect("just appended accepted sample"))?;
    }
    Ok(ConfiguredResult{samples,report:IntegrationReport{method:options.method,initialization:if options.initial_state.is_some(){"explicit_state"}else{"operating_point"},accepted_steps:steps,rejected_steps:rejected,attempts,max_accepted_error:max_error}})
}

