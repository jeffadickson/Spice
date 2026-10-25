use circuit_simulator::{api::{Request,run},assertion::Outcome,experiment::{ExperimentSpec,MeasurementSpec,Operation,AssertionSpec}};
#[test]fn all_recorded_window_operations_and_indeterminate_policy(){
    let mut r:Request=serde_json::from_str(include_str!("../examples/adaptive-rc.json")).unwrap();
    r.experiment=Some(ExperimentSpec{measurements:vec![
        MeasurementSpec{id:"min".into(),signal:"v(a)".into(),operation:Operation::Minimum{start_s:0.,stop_s:0.005}},
        MeasurementSpec{id:"max".into(),signal:"v(a)".into(),operation:Operation::Maximum{start_s:0.,stop_s:0.005}},
        MeasurementSpec{id:"integral".into(),signal:"v(a)".into(),operation:Operation::Integral{start_s:0.,stop_s:0.005}},
        MeasurementSpec{id:"missing".into(),signal:"v(nope)".into(),operation:Operation::ValueAt{time_s:0.005}}
    ],assertions:vec![AssertionSpec{id:"unknown".into(),measurement:"missing".into(),unit:"V".into(),low:0.,high:1.},AssertionSpec{id:"wrong-unit".into(),measurement:"max".into(),unit:"A".into(),low:0.,high:2.}]});
    let out=run(&r).unwrap();assert!(out.measurements[0].value.unwrap()<0.007);assert_eq!(out.measurements[1].value,Some(1.));assert_eq!(out.measurements[2].unit,"V*s");assert!(out.measurements[2].value.unwrap()>0.00099);
    assert!(out.assertions.iter().all(|a|a.outcome==Outcome::Indeterminate));assert!(out.complete);
}
#[test]fn adaptive_lu_and_sparse_provider_match(){
    let mut r:Request=serde_json::from_str(include_str!("../examples/adaptive-rc.json")).unwrap();let sparse=run(&r).unwrap();r.backend="lu".into();let dense=run(&r).unwrap();
    let a=sparse.samples.last().unwrap().values[0];let b=dense.samples.last().unwrap().values[0];assert!((a-b).abs()<1e-9);
}
#[test]fn invalid_experiment_does_not_run(){
    let mut r:Request=serde_json::from_str(include_str!("../examples/adaptive-rc.json")).unwrap();
    let e=r.experiment.as_mut().unwrap();e.measurements.push(e.measurements[0].clone());assert!(run(&r).is_err());
}

