//! Memoryless constitutive laws must track actual source time, including chained domains.
use circuit_simulator::api::{Request,run};
fn request(backend:&str)->Request{serde_json::from_value(serde_json::json!({"schema_version":"1","case_id":"behavior_time","backend":backend,"circuit":{"nodes":["0","in","ex","log"],"elements":[
{"kind":"voltage","id":"V","p":1,"n":0,"volts":2.0},
{"kind":"behavioral_voltage","id":"Bexp","p":2,"n":0,"cp":1,"cn":0,"expression":{"op":"exp","argument":{"op":"input"}}},
{"kind":"behavioral_voltage","id":"Blog","p":3,"n":0,"cp":2,"cn":0,"expression":{"op":"log","argument":{"op":"input"}}},
{"kind":"resistor","id":"R","p":3,"n":0,"ohms":1000.0}],},"analysis":{"kind":"transient_configured","options":{"stop_s":0.001,"max_step_s":0.0001,"max_steps":100,"sources":[{"id":"V","waveform":{"kind":"pwl","points":[{"time_s":0.0,"value":2.0},{"time_s":0.001,"value":3.0}]}}]}}})).unwrap()}
#[test]fn chained_exp_log_tracks_time_and_current_on_both_cpu_providers(){for backend in ["lu","faer"]{let out=run(&request(backend)).unwrap();assert!(out.samples.len()>5);let branch=out.signals.iter().position(|s|s.id=="i(Blog)").unwrap();for sample in &out.samples{let input=2.+1000.*sample.time_s;assert!((sample.values[0]-input).abs()<1e-9);assert!((sample.values[1]-input.exp()).abs()<1e-8);assert!((sample.values[2]-input).abs()<1e-9);assert!((sample.values[branch]+input/1000.).abs()<1e-12);}assert!(!out.consistency.has_failures());}}

#[test]fn unsupported_profiles_and_startup_domains_remain_explicit(){
 for mode in 0..6 {
  let mut value=serde_json::to_value(request("faer")).unwrap();
  match mode {
   0=>{value["analysis"]["options"]["method"]="trapezoidal".into();},
   1=>{value["circuit"]["elements"].as_array_mut().unwrap().push(serde_json::json!({"kind":"capacitor","id":"C","p":3,"n":0,"farads":1e-9}));},
   2=>{value["circuit"]["elements"][1]["cp"]=3.into();},
   3=>{value["circuit"]["elements"][1]["expression"]["op"]="log".into();value["circuit"]["elements"][0]["volts"]=0.0.into();value["analysis"]["options"]["sources"][0]["waveform"]["points"][0]["value"]=0.0.into();}
   4=>{let mut policy=circuit_simulator::newton::NewtonOptions::default();policy.continuation=Some(Default::default());value["analysis"]["options"]["initial_newton"]=serde_json::to_value(policy).unwrap();value["circuit"]["elements"][1]["cp"]=3.into();for i in [1,2]{value["circuit"]["elements"][i]["expression"]=serde_json::json!({"op":"input"});}}
   _=>{value["analysis"]["options"]["initial_state"]=serde_json::json!([2.0,2.0_f64.exp(),2.0,0.0,0.0,-0.002]);}
  }
  let bad:Request=serde_json::from_value(value).unwrap();assert!(run(&bad).is_err(),"unsupported mode {mode}");
  let plan=circuit_simulator::compile::compile(&bad.circuit).unwrap();
  if let circuit_simulator::api::Analysis::TransientConfigured{options}=bad.analysis {
   let error=configured_error(&plan,&options);
   if [0,1,4,5].contains(&mode){assert_eq!(error.code,circuit_simulator::error::ErrorCode::Unsupported,"direct configured mode {mode}");}
  }
 }
}


#[test]fn public_legacy_transient_keeps_its_behavioral_refusal(){
 let mut value=serde_json::to_value(request("faer")).unwrap();
 for i in [1,2]{value["circuit"]["elements"][i]["expression"]=serde_json::json!({"op":"input"});}
 let request:Request=serde_json::from_value(value).unwrap();let plan=circuit_simulator::compile::compile(&request.circuit).unwrap();
 let options=circuit_simulator::transient::TransientOptions{stop_s:0.001,step_s:0.0001,max_steps:100};
 assert!(circuit_simulator::transient::transient(&plan,&circuit_simulator::backend::DenseLu,&options).is_err());
}
#[test]fn passive_mediated_behavioral_controls_are_an_explicit_unsupported_profile(){
 let mut value=serde_json::to_value(request("faer")).unwrap();
 value["circuit"]["nodes"].as_array_mut().unwrap().push("mid".into());
 value["circuit"]["elements"][2]["cp"]=4.into();
 let elements=value["circuit"]["elements"].as_array_mut().unwrap();
 elements.push(serde_json::json!({"kind":"resistor","id":"RD1","p":2,"n":4,"ohms":1000.0}));
 elements.push(serde_json::json!({"kind":"resistor","id":"RD2","p":4,"n":0,"ohms":1000.0}));
 let request:Request=serde_json::from_value(value).unwrap();
 assert_eq!(run(&request).unwrap_err().code,circuit_simulator::error::ErrorCode::Unsupported);
}

fn configured_error(plan:&circuit_simulator::compile::CompiledCircuit,options:&circuit_simulator::configured::TransientConfig)->circuit_simulator::error::SimError {
 match circuit_simulator::configured::run(plan,&circuit_simulator::backend_faer::FaerSparseLu,options,&false.into()) {
  Err(error)=>error,Ok(_)=>panic!("unsupported direct configured profile succeeded")
 }
}
