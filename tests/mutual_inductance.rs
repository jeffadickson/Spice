use circuit_simulator::{circuit::Circuit,compile::compile,backend::DenseLu,backend_faer::FaerSparseLu,ac::ac};
fn circuit(k:f64)->Circuit{serde_json::from_value(serde_json::json!({"nodes":["0","in","load","coil"],"elements":[
{"kind":"voltage","id":"V","p":1,"n":0,"volts":1.0},
{"kind":"resistor","id":"R","p":1,"n":3,"ohms":10.0},
{"kind":"inductor","id":"L1","p":3,"n":0,"henries":0.001},
{"kind":"inductor","id":"L2","p":2,"n":0,"henries":0.004},
{"kind":"resistor","id":"RL","p":2,"n":0,"ohms":40.0},
{"kind":"mutual_inductance","id":"K","p":0,"n":0,"first":"L1","second":"L2","coefficient":k}]})).unwrap()}
#[test]fn coupled_ac_matches_independent_two_by_two_equations(){
 for k in [-0.75,0.,0.75,1.]{let p=compile(&circuit(k)).unwrap();for solver in [&DenseLu as &dyn circuit_simulator::linalg::LinearSolver,&FaerSparseLu]{
 let result=ac(&p,&[1000.],solver).unwrap();
 let audit=circuit_simulator::audit::ac(&p.circuit(),&result[0],&[],circuit_simulator::audit::AuditOptions::default()).unwrap();assert!(!audit.has_failures(),"{audit:?}"); let w=std::f64::consts::TAU*1000.;let m=k*0.001_f64.sqrt()*0.004_f64.sqrt();
 let j=num_complex::Complex64::new(0.,1.);let z1=j*w*0.001;let z2=j*w*0.004;let zm=j*w*m;
 let i1=1./(10.+z1-zm*zm/(40.+z2));let i2=-zm*i1/(40.+z2);let out=-40.*i2;
 assert!((result[0].values[1].real-out.re).abs()<1e-10);assert!((result[0].values[1].imag-out.im).abs()<1e-10);}}
}
#[test]fn coupling_contract_rejects_missing_self_and_nonpassive_groups(){
 for (first,second,k) in [("L1","missing",0.5),("L1","L1",0.5),("L1","L2",1.1)]{let mut v=serde_json::to_value(circuit(0.5)).unwrap();v["elements"][5]["first"]=first.into();v["elements"][5]["second"]=second.into();v["elements"][5]["coefficient"]=k.into();let c:Circuit=serde_json::from_value(v).unwrap();assert!(compile(&c).is_err());}
}

#[test]
fn configured_coupling_flux_energy_and_signed_current_match_independent_recurrence() {
 use circuit_simulator::{configured::{run,Method,TransientConfig},audit::{self,AuditOptions}};
 let c=circuit(-0.75);let p=compile(&c).unwrap();let l1=0.001;let l2=0.004;let m=-0.0015;
 for method in [Method::BackwardEuler,Method::Trapezoidal] {
  let h=1e-6;let options=TransientConfig{stop_s:1e-4,max_step_s:h,max_steps:200,method,current_absolute_tolerance:None,accepted_times_s:None,initial_newton:None,initial_state:Some(vec![1.,-0.4,0.5,-0.05,0.05,0.01]),sources:vec![],adaptive:None};
  let result=run(&p,&FaerSparseLu,&options,&false.into()).unwrap();let mut i1=0.05;let mut i2=0.01;
  for pair in result.samples.windows(2) {
   let dt=pair[1].time_s-pair[0].time_s;let scale=if matches!(method,Method::BackwardEuler){1.}else{0.5};
   let a=l1+scale*dt*10.;let b=l2+scale*dt*40.;let determinant=a*b-m*m;
   let r1=(l1-(1.-scale)*dt*10.)*i1+m*i2+dt;
   let r2=m*i1+(l2-(1.-scale)*dt*40.)*i2;
   i1=(b*r1-m*r2)/determinant;i2=(a*r2-m*r1)/determinant;
   assert!((pair[1].values[p.branch_index("L1").unwrap()]-i1).abs()<1e-11);
   assert!((pair[1].values[p.branch_index("L2").unwrap()]-i2).abs()<1e-11);
   let report=audit::configured_step(&c,&pair[0],&pair[1],&options,AuditOptions::default()).unwrap();assert!(!report.has_failures(),"{report:?}");
   let mut corrupt=pair[1].clone();corrupt.values[p.branch_index("L2").unwrap()]+=0.001;
   assert!(audit::configured_step(&c,&pair[0],&corrupt,&options,AuditOptions::default()).unwrap().has_failures());
  }
 }
}

#[test]fn pairwise_legal_but_negative_energy_group_is_rejected(){
 let mut c=circuit(0.9);c.elements.push(circuit_simulator::circuit::Element::Inductor{id:"L3".into(),p:circuit_simulator::ids::NodeId(2),n:circuit_simulator::ids::NodeId(0),henries:0.009});
 for(first,second,k)in [("L1","L3",0.9),("L2","L3",-0.9)]{c.elements.push(circuit_simulator::circuit::Element::MutualInductance{id:format!("{first}{second}"),p:circuit_simulator::ids::NodeId(0),n:circuit_simulator::ids::NodeId(0),first:first.into(),second:second.into(),coefficient:k});}
 assert!(compile(&c).is_err());
}

proptest::proptest!{
 #![proptest_config(proptest::test_runner::Config{cases:128,rng_seed:proptest::test_runner::RngSeed::Fixed(2026111520),..Default::default()})]
 #[test]fn signed_pair_energy_is_nonnegative(k in -1f64..1.,i in -10f64..10.,j in -10f64..10.){
  let c=circuit(k);c.validate().unwrap();let m=circuit_simulator::mutual_inductance::pairs(&c).unwrap()[0].2;
  let energy=0.5*0.001*i*i+m*i*j+0.5*0.004*j*j;
  proptest::prop_assert!(energy>=-1e-14);
 }
}
