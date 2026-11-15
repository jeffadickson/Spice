//! Accepted-grid replay isolates linear physics from adaptive-controller choices.
use circuit_simulator::{backend::DenseLu, compile::compile, configured::{self,TransientConfig},netlist::parse};
use proptest::prelude::*;

fn options(grid:&str)->TransientConfig {
    serde_json::from_str(&format!(r#"{{"stop_s":1.0,"max_step_s":0.5,"max_steps":10,"method":"trapezoidal","accepted_times_s":{grid},"sources":[{{"id":"V1","waveform":{{"kind":"pwl","points":[{{"time_s":0.0,"value":0.0}},{{"time_s":1.0,"value":1.0}}]}}}}]}}"#)).unwrap()
}

#[test]
fn irregular_accepted_times_reach_the_real_source_and_circuit_without_interpolation() {
    let plan=compile(&parse("title\nV1 a 0 0\nR1 a 0 2").unwrap()).unwrap();
    let result=configured::run(&plan,&DenseLu,&options("[0,0.1,0.35,0.5,1]"),&false.into()).unwrap();
    assert_eq!(result.samples.iter().map(|p|p.time_s).collect::<Vec<_>>(),vec![0.,0.1,0.35,0.5,1.]);
    for p in &result.samples {assert!((p.values[0]-p.time_s).abs()<1e-12);assert!((p.values[1]+p.time_s/2.).abs()<1e-12);}
    assert_eq!(serde_json::to_value(result.report).unwrap()["time_control"],"accepted_time_grid");
}

#[test]
fn malformed_oversized_or_incomplete_grids_and_adaptive_conflicts_are_refused() {
    for bad in ["[]","[0]","[0,0.5]","[0.1,0.5,1]","[0,0.5,0.4,1]","[0,0.5,0.5,1]","[0,0.75,1]"] {
        assert!(options(bad).validate().is_err(),"{bad}");
    }
    let mut json=serde_json::to_value(options("[0,0.5,1]")).unwrap();
    json["adaptive"]=serde_json::json!({"relative":0.001,"voltage_absolute":1e-6,"current_absolute":1e-9,"min_step_s":1e-9,"max_attempts":100});
    assert!(serde_json::from_value::<TransientConfig>(json).unwrap().validate().is_err());
}

#[test]
fn a_missing_authored_source_corner_fails_before_publishing_any_sample() {
    let plan=compile(&parse("title\nV1 a 0 0\nR1 a 0 2").unwrap()).unwrap();
    let mut json=serde_json::to_value(options("[0,0.5,1]")).unwrap();
    json["sources"][0]["waveform"]["points"]=serde_json::json!([{"time_s":0.,"value":0.},{"time_s":0.25,"value":1.},{"time_s":1.,"value":0.}]);
    let config=serde_json::from_value::<TransientConfig>(json).unwrap();let mut count=0;
    assert!(configured::run_observed(&plan,&DenseLu,&config,&false.into(),|_|{count+=1;Ok(())}).is_err());assert_eq!(count,0);
}

#[test]
fn nonlinear_replay_is_refused_before_any_sample() {
    let plan=compile(&parse("V1 a 0 0\nD1 a 0 1e-12 .025").unwrap()).unwrap();let mut count=0;
    let result=configured::run_observed(&plan,&DenseLu,&options("[0,0.5,1]"),&false.into(),|_|{count+=1;Ok(())});
    assert_eq!(result.err().unwrap().code,circuit_simulator::error::ErrorCode::Unsupported);assert_eq!(count,0);
}

#[test]
fn explicit_grid_exceeding_scalar_storage_is_refused_before_any_sample() {
    let text=(1..=20).map(|i|format!("R{i} n{i} 0 1\n")).collect::<String>();
    let plan=compile(&parse(&("title\n".to_owned()+&text)).unwrap()).unwrap();
    let json=serde_json::json!({"stop_s":100000.,"max_step_s":1.,"max_steps":100000,"accepted_times_s":(0..=100000).map(|i|i as f64).collect::<Vec<_>>()});
    let options:TransientConfig=serde_json::from_value(json).unwrap();let mut observed=0;
    let result=configured::run_observed(&plan,&DenseLu,&options,&false.into(),|_|{observed+=1;Ok(())});
    assert_eq!(result.err().unwrap().code,circuit_simulator::error::ErrorCode::ResourceExhausted);assert_eq!(observed,0);
}

proptest! {
    #![proptest_config(ProptestConfig{cases:64,rng_seed:proptest::test_runner::RngSeed::Fixed(2026111406),..ProptestConfig::default()})]
    #[test]
    fn every_random_accepted_time_is_preserved_and_a_duplicate_is_refused(weights in prop::collection::vec(1u8..32,2..16)) {
        let total:usize=weights.iter().map(|v|usize::from(*v)).sum();let mut sum=0usize;let mut times=vec![0.];
        for weight in weights {sum+=usize::from(weight);times.push(sum as f64/total as f64);}
        let mut json=serde_json::to_value(options("[0,0.5,1]")).unwrap();json["max_step_s"]=serde_json::json!(1.);json["max_steps"]=serde_json::json!(32);
        json["accepted_times_s"]=serde_json::json!(times);
        let config:TransientConfig=serde_json::from_value(json.clone()).unwrap();
        let plan=compile(&parse("V1 a 0 0\nR1 a 0 2").unwrap()).unwrap();
        let run=configured::run(&plan,&DenseLu,&config,&false.into()).unwrap();
        prop_assert_eq!(run.samples.iter().map(|p|p.time_s).collect::<Vec<_>>(),times.clone());
        for sample in run.samples {prop_assert!((sample.values[0]-sample.time_s).abs()<1e-12);}
        json["accepted_times_s"][1]=serde_json::json!(0.);
        prop_assert!(serde_json::from_value::<TransientConfig>(json).unwrap().validate().is_err());
    }
}
