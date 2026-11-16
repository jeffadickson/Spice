use circuit_simulator::{circuit::Circuit,compile::compile,backend::DenseLu,configured::{self,TransientConfig}};
#[test]
fn relative_bound_is_validated_and_reported_without_weakening_defaults(){
 let circuit:Circuit=serde_json::from_value(serde_json::json!({"nodes":["0","a"],"elements":[{"kind":"voltage","id":"V","p":1,"n":0,"volts":1.0},{"kind":"resistor","id":"R","p":1,"n":0,"ohms":1000.0}]})).unwrap();
 let plan=compile(&circuit).unwrap();
 for bound in [1e-5_f64,1e-12]{
  let options:TransientConfig=serde_json::from_value(serde_json::json!({"stop_s":0.001,"max_step_s":0.0001,"max_steps":100,"relative_tolerance":bound})).unwrap();
  let result=configured::run(&plan,&DenseLu,&options,&false.into()).unwrap();
  let report=serde_json::to_value(result.report).unwrap();
  assert_eq!(report["effective_relative_tolerance"].as_f64().unwrap(),bound.min(1e-9));
 }
 for value in [0.0,-0.1]{let options:TransientConfig=serde_json::from_value(serde_json::json!({"stop_s":0.001,"max_step_s":0.0001,"max_steps":100,"relative_tolerance":value})).unwrap();assert!(options.validate().is_err());}
}

proptest::proptest! {
 #![proptest_config(proptest::test_runner::Config::with_cases(64))]
 #[test]
 fn positive_relative_upper_bounds_validate(exponent in -14.0_f64..-3.0){
  let value=10.0_f64.powf(exponent);
  let options:TransientConfig=serde_json::from_value(serde_json::json!({"stop_s":0.001,"max_step_s":0.0001,"max_steps":100,"relative_tolerance":value})).unwrap();
  proptest::prop_assert!(options.validate().is_ok());
  proptest::prop_assert_eq!(options.relative_tolerance,Some(value));
 }
}
