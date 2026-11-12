//! Nonlinear trapezoidal steps checked against an independent scalar equation.
use circuit_simulator::{backend::DenseLu,compile::compile,netlist::parse,newton::{self,NewtonOptions}};
#[test]
fn diode_rc_trapezoidal_matches_independent_bisection(){
 let plan=compile(&parse("V1 in 0 .5\nR1 in out 1000\nC1 out 0 1u\nD1 out 0 1e-12 .025").unwrap()).unwrap();
 // Explicit finite history satisfies source constraints. The nonlinear method
 // must use previous conduction as well as charge, not silently take a BE step.
 let previous=[0.5,0.2,-0.0003];let rhs=plan.rhs().to_vec();let h=1e-4;
 let (actual,_)=newton::solve_trapezoidal_step(&plan,&DenseLu,NewtonOptions::default(),&previous,h,&rhs,&rhs).unwrap();
 let f=|v:f64|(v-0.5)/1000.+1e-12*((v/0.025).exp()-1.);
 let mut left=0.;let mut right=0.5;for _ in 0..100{let mid=(left+right)/2.;let residual=f(mid)+f(0.2)+2e-6*(mid-0.2)/h;if residual>0.{right=mid}else{left=mid}}
 assert!((actual[1]-(left+right)/2.).abs()<1e-10);
 let(be,_)=newton::solve_backward_euler_step(&plan,&DenseLu,NewtonOptions::default(),&previous,h,&rhs).unwrap();
 assert!((actual[1]-be[1]).abs()>1e-4);
}


#[test]
fn source_corners_restart_first_order_without_trapezoidal_current_ringing(){
 use circuit_simulator::{configured::{TransientConfig,Method,run},sources::SourceOverride,waveform::Waveform};
 let plan=compile(&parse("V1 x 0 0\nC1 x 0 1u\nD1 0 x 1e-30 .025").unwrap()).unwrap();
 let config=TransientConfig{stop_s:4e-6,max_step_s:1e-7,max_steps:100,method:Method::Trapezoidal,
 sources:vec![SourceOverride{id:"V1".into(),waveform:Waveform::Pulse{low:0.,high:1.,delay_s:1e-6,rise_s:1e-6,fall_s:1e-6,width_s:1e-6,period_s:4e-6}}],..Default::default()};
 let result=run(&plan,&DenseLu,&config,&false.into()).unwrap();
 for sample in &result.samples{
  if sample.time_s>1.01e-6 && sample.time_s<1.99e-6{assert!((sample.values[1]+1.).abs()<1e-9,"{} {}",sample.time_s,sample.values[1]);}
  if sample.time_s>2.01e-6 && sample.time_s<2.99e-6{assert!(sample.values[1].abs()<1e-9);}
 }
 assert!(result.report.backward_euler_restarts.len()>=3);
}


proptest::proptest! {
 #![proptest_config(proptest::test_runner::Config{cases:64,rng_seed:proptest::test_runner::RngSeed::Fixed(2026111202),..Default::default()})]
 #[test]
 fn seeded_diode_rc_endpoint_equation(r in 100f64..10000., c in 1e-8f64..1e-6,h in 1e-6f64..1e-3,old in 0f64..0.2,drive in 0.3f64..0.5){
  let plan=compile(&parse(&format!("V1 in 0 {drive}\nR1 in out {r}\nC1 out 0 {c}\nD1 out 0 1e-12 .025")).unwrap()).unwrap();
  let previous=[drive,old,-(drive-old)/r];let rhs=plan.rhs();
  let(actual,_)=newton::solve_trapezoidal_step(&plan,&DenseLu,NewtonOptions::default(),&previous,h,rhs,rhs).unwrap();
  let f=|v:f64|(v-drive)/r+1e-12*((v/0.025).exp()-1.);
  let mut left=-0.5;let mut right=1.;for _ in 0..100{let mid=(left+right)/2.;if f(mid)+f(old)+2.*c*(mid-old)/h>0.{right=mid}else{left=mid}}
  proptest::prop_assert!((actual[1]-(left+right)/2.).abs()<1e-8);
 }
}

#[test]
fn malformed_trapezoidal_history_and_step_are_refused(){
 let plan=compile(&parse("V1 x 0 0\nD1 x 0 1e-12 .025").unwrap()).unwrap();
 let rhs=plan.rhs();
 for h in [0.,-1.,f64::NAN,f64::INFINITY]{assert!(newton::solve_trapezoidal_step(&plan,&DenseLu,NewtonOptions::default(),&[0.,0.],h,rhs,rhs).is_err());}
 assert!(newton::solve_trapezoidal_step(&plan,&DenseLu,NewtonOptions::default(),&[0.],1.,rhs,rhs).is_err());
 assert!(newton::solve_trapezoidal_step(&plan,&DenseLu,NewtonOptions::default(),&[0.,0.],1.,rhs,&[f64::NAN,0.]).is_err());
}

#[test]
fn loose_requested_current_bound_cannot_weaken_the_physical_output_floor(){
 use circuit_simulator::configured::{TransientConfig,run};
 let plan=compile(&parse("V1 x 0 0\nD1 0 x 1e-12 .025").unwrap()).unwrap();
 let mut config=TransientConfig{stop_s:1e-6,max_step_s:1e-6,current_absolute_tolerance:Some(1e-6),..Default::default()};
 let loose=run(&plan,&DenseLu,&config,&false.into()).unwrap();
 assert_eq!(loose.report.effective_current_absolute_tolerance,1e-12);
 config.current_absolute_tolerance=Some(1e-15);
 let tight=run(&plan,&DenseLu,&config,&false.into()).unwrap();
 assert_eq!(tight.report.effective_current_absolute_tolerance,1e-15);
}

#[test]
fn decimal_corner_and_fixed_grid_do_not_create_an_ulp_sized_step(){
 use circuit_simulator::{configured::{TransientConfig,Method,run},sources::SourceOverride,waveform::{Waveform,Point}};
 let plan=compile(&parse("V1 x 0 0\nC1 x 0 1u\nD1 0 x 1e-30 .025").unwrap()).unwrap();
 let config=TransientConfig{stop_s:0.5,max_step_s:0.1,max_steps:10,method:Method::Trapezoidal,
 sources:vec![SourceOverride{id:"V1".into(),waveform:Waveform::Pwl{points:vec![Point{time_s:0.,value:0.},Point{time_s:0.3,value:1.},Point{time_s:0.5,value:1.}]}}],..Default::default()};
 let result=run(&plan,&DenseLu,&config,&false.into()).unwrap();
 assert_eq!(result.samples.len(),6);
 assert!(result.samples.windows(2).all(|p|p[1].time_s-p[0].time_s>0.09));
}

#[test]
fn charged_junction_api_currents_and_independent_audits_use_the_restart_history(){check_junction_api(false)}
#[test]
fn adaptive_charged_junction_keeps_accepted_half_step_currents_and_audits(){check_junction_api(true)}
fn check_junction_api(adaptive:bool){
 use circuit_simulator::{api,configured::Method,audit::{self,AuditOptions}};
 let mut request:api::Request=serde_json::from_value(serde_json::json!({
 "schema_version":"1","case_id":"junction-trap","backend":"lu",
 "circuit":{"nodes":["0","x"],"elements":[
 {"kind":"voltage","id":"V1","p":1,"n":0,"volts":0.},
 {"kind":"spice_diode","id":"D1","p":0,"n":1,"model":{"saturation_amps":1e-14,"thermal_volts":0.025,"junction_farads":1e-12,"junction_volts":0.8,"grading":0.5,"forward_coefficient":0.5,"transit_seconds":0.}}]},
 "analysis":{"kind":"transient_configured","options":{"stop_s":4e-6,"max_step_s":1e-7,"max_steps":100,"method":"trapezoidal","current_absolute_tolerance":1e-6,"sources":[{"id":"V1","waveform":{"kind":"pulse","low":0.,"high":0.2,"delay_s":1e-6,"rise_s":1e-6,"fall_s":1e-6,"width_s":1e-6,"period_s":4e-6}}]}}
 })).unwrap();
 if adaptive { if let api::Analysis::TransientConfigured{options}= &mut request.analysis {options.adaptive=Some(circuit_simulator::adaptive::AdaptiveOptions { current_relative: None,relative:1e-5,voltage_absolute:1e-7,current_absolute:1e-10,min_step_s:1e-13,max_attempts:10000});options.max_steps=10000;} }
 let result=api::run(&request).unwrap();
 let source=result.signals.iter().position(|s|s.id=="i(V1)").unwrap();
 let junction=result.signals.iter().position(|s|s.id=="i(D1)").unwrap();
 for s in &result.samples {assert!((s.values[source]-s.values[junction]).abs()<1e-12);}
 assert!(result.consistency.checks.iter().any(|c|c.id.starts_with("kcl:")));
 let api::Analysis::TransientConfigured{mut options}=request.analysis else{unreachable!()};
 let report=result.integration.unwrap();if adaptive {assert_eq!(report.retained_midpoints,report.accepted_steps);assert_eq!(result.samples.len(),report.accepted_steps+1);options.adaptive=None;}
 let before=&result.samples[2];let mut corrupted=result.samples[3].clone();
 // Audit inputs are complete MNA states, excluding derived observer columns.
 let n=request.circuit.nodes.len()-1+1;
 corrupted.values.truncate(n);corrupted.values[source]+=1e-3;
 let mut previous=before.clone();previous.values.truncate(n);
 if report.backward_euler_restarts.contains(&corrupted.time_s){options.method=Method::BackwardEuler;}
 assert!(audit::configured_step(&request.circuit,&previous,&corrupted,&options,AuditOptions::default()).unwrap().has_failures());
}

#[test]
fn direct_trapezoidal_call_cannot_bypass_unqualified_meyer_charge_admission(){
 use circuit_simulator::{api::Request,circuit::Element,error::ErrorCode};
 let mut request:Request=serde_json::from_str(include_str!("fixtures/mosmem_initial_gmin.json")).unwrap();
 let mut changed=false;
 for element in &mut request.circuit.elements {if let Element::Mos1Channel{model,..}=element{model.oxide_farads=1e-12;changed=true;break;}}
 assert!(changed);let plan=compile(&request.circuit).unwrap();
 let error=newton::solve_trapezoidal_step(&plan,&DenseLu,NewtonOptions::default(),&vec![0.;plan.unknown_count()],1e-9,plan.rhs(),plan.rhs()).unwrap_err();
 assert_eq!(error.code,ErrorCode::Unsupported);
}

#[test]
fn inconsistent_old_source_voltage_is_refused_instead_of_doubling_the_next_voltage(){
 let plan=compile(&parse("V1 x 0 1\nD1 x 0 1e-30 .025").unwrap()).unwrap();
 let error=newton::solve_trapezoidal_step(&plan,&DenseLu,NewtonOptions::default(),&[0.,0.],1e-6,plan.rhs(),plan.rhs()).unwrap_err();
 assert_eq!(error.code,circuit_simulator::error::ErrorCode::InvalidInput);
 assert!(error.message.contains("algebraic voltage constraint"));
}

#[test]
fn adaptive_numerical_retry_keeps_committed_history_and_counts_failed_provider_calls(){
 use circuit_simulator::{configured::{TransientConfig,Method,run_observed},sources::SourceOverride,waveform::Waveform,linalg::{LinearSolver,Matrix},error::{SimResult,SimError,ErrorCode}};
 use std::sync::atomic::{AtomicUsize,Ordering};
 struct FailOnce(AtomicUsize);
 impl LinearSolver for FailOnce {
  fn name(&self)->&'static str{"fail_once_recording_lu"}
  fn solve(&self,a:&Matrix,b:&[f64])->SimResult<Vec<f64>>{
   if self.0.fetch_add(1,Ordering::SeqCst)==0{return Err(SimError::new(ErrorCode::NumericalFailure,"injected trial failure"));}
   DenseLu.solve(a,b)
  }
 }
 let plan=compile(&parse("V1 x 0 0\nC1 x 0 1u\nD1 0 x 1e-30 .025").unwrap()).unwrap();
 let config=TransientConfig{stop_s:1e-6,max_step_s:2e-7,max_steps:100,method:Method::Trapezoidal,
 sources:vec![SourceOverride{id:"V1".into(),waveform:Waveform::Pulse{low:0.,high:0.1,delay_s:0.,rise_s:1e-6,fall_s:1e-6,width_s:1e-6,period_s:3e-6}}],
 adaptive:Some(circuit_simulator::adaptive::AdaptiveOptions { current_relative: None,relative:1e-5,voltage_absolute:1e-7,current_absolute:1e-9,min_step_s:1e-13,max_attempts:1000}),..Default::default()};
 let provider=FailOnce(AtomicUsize::new(0));let mut observed=Vec::new();
 let result=run_observed(&plan,&provider,&config,&false.into(),|s|{observed.push(s.time_s);Ok(())}).unwrap();
 assert_eq!(result.report.failed_nonlinear_trials,1);
 assert_eq!(result.report.linear_solves,provider.0.load(Ordering::SeqCst));
 assert_eq!(result.report.retained_midpoints,result.report.accepted_steps);
 assert_eq!(result.midpoint_history.len(),result.report.accepted_steps);
 assert_eq!(observed,result.samples.iter().map(|s|s.time_s).collect::<Vec<_>>());
 assert_eq!(result.report.attempts,result.report.accepted_steps+result.report.rejected_steps);
}

#[test]
fn valid_old_inductor_voltage_is_dynamic_history_not_a_dc_constraint(){
 let plan=compile(&parse("V1 in 0 1\nR1 in out 10\nL1 out 0 1\nD1 0 out 1e-30 .025").unwrap()).unwrap();
 let previous=[1.,0.8,-0.02,0.02];let h=0.1;
 let(actual,_)=newton::solve_trapezoidal_step(&plan,&DenseLu,NewtonOptions::default(),&previous,h,plan.rhs(),plan.rhs()).unwrap();
 let expected=(0.02+h/2.*(0.8+1.))/(1.+10.*h/2.);
 assert!((actual[3]-expected).abs()<1e-10);
 assert!((actual[1]-(1.-10.*expected)).abs()<1e-10);
}

#[test]
fn adaptive_local_error_rejections_retain_only_accepted_nonlinear_rc_midpoints(){
 use circuit_simulator::{configured::{TransientConfig,Method,run},sources::SourceOverride,waveform::Waveform,adaptive::AdaptiveOptions};
 let plan=compile(&parse("V1 in 0 0\nR1 in out 1000\nC1 out 0 1n\nD1 0 out 1e-30 .025").unwrap()).unwrap();
 let config=TransientConfig{stop_s:1e-6,max_step_s:2e-7,max_steps:10000,method:Method::Trapezoidal,
 sources:vec![SourceOverride{id:"V1".into(),waveform:Waveform::Pulse{low:0.,high:0.2,delay_s:0.,rise_s:1e-7,fall_s:1e-7,width_s:5e-7,period_s:1e-6}}],
 adaptive:Some(AdaptiveOptions { current_relative: None,relative:1e-6,voltage_absolute:1e-8,current_absolute:1e-10,min_step_s:1e-13,max_attempts:40000}),..Default::default()};
 let result=run(&plan,&DenseLu,&config,&false.into()).unwrap();
 assert!(result.report.rejected_steps>0);
 assert_eq!(result.report.retained_midpoints,result.report.accepted_steps);
 assert_eq!(result.midpoint_history.len(),result.report.accepted_steps);
 assert_eq!(result.report.attempts,result.report.accepted_steps+result.report.rejected_steps);
 assert!(result.midpoint_history.iter().zip(result.samples.windows(2)).all(|(m,p)|m.as_ref().is_some_and(|m|m.time_s>p[0].time_s&&m.time_s<p[1].time_s)));
}
