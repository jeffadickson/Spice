//! Positive and negative admission cases distinguish independent API predicates.
use circuit_simulator::{api::{self, Analysis, Request}, configured::TransientConfig,
    error::ErrorCode, netlist::parse};

#[test]
fn case_id_limit_is_inclusive_in_bytes_and_preserved_in_the_manifest() {
    let base:Request=serde_json::from_str(include_str!("../examples/divider.json")).unwrap();
    for id in ["x".repeat(256),"é".repeat(128)] {
        let request=Request{case_id:id.clone(),..base.clone()};
        let result=api::run(&request).unwrap();assert_eq!(result.case_id,id);
    }
    for id in [String::new(),"x".repeat(257)] {
        assert_eq!(api::run(&Request{case_id:id,..base.clone()}).unwrap_err().code,ErrorCode::InvalidInput);
    }
}

#[test]
fn charged_junction_and_explicit_initial_state_are_a_joint_refusal() {
    let mut base:Request=serde_json::from_str(include_str!("../examples/divider.json")).unwrap();
    // An ordinary RC with an explicit capacitor voltage remains executable.
    base.circuit=parse("RC\nI1 0 a 0\nR1 a 0 1000\nC1 a 0 1u\n.end").unwrap();
    base.analysis=Analysis::TransientConfigured{options:TransientConfig{stop_s:2e-4,max_step_s:1e-4,
        initial_state:Some(vec![0.25]),..Default::default()}};
    let result=api::run(&base).unwrap();
    let voltage=result.signals.iter().position(|s|s.id=="v(a)").unwrap();
    let expected=0.25/(1.1_f64*1.1);
    assert!((result.samples.last().unwrap().values[voltage]-expected).abs()<1e-11);
    // Junction charge must not prohibit an operating point. Charge is inactive
    // in DC; the independent reference is the Shockley conduction law.
    base.circuit=serde_json::from_value(serde_json::json!({"nodes":["0","a"],"elements":[
        {"kind":"voltage","id":"V1","p":1,"n":0,"volts":0.05},
        {"kind":"spice_diode","id":"D1","p":1,"n":0,"model":{
            "saturation_amps":1e-9,"thermal_volts":0.025,"junction_farads":1e-9,
            "junction_volts":0.8,"grading":0.5,"forward_coefficient":0.5,"transit_seconds":0.}}
    ]})).unwrap();
    base.analysis=Analysis::OperatingPoint;
    let result=api::run(&base).unwrap();
    let current=result.signals.iter().position(|s|s.id=="i(V1)").unwrap();
    assert!((result.samples[0].values[current]+1e-9*2_f64.exp_m1()).abs()<1e-12);
    base.analysis=Analysis::TransientConfigured{options:TransientConfig{initial_state:Some(vec![0.05,0.]),..Default::default()}};
    assert_eq!(api::run(&base).unwrap_err().code,ErrorCode::Unsupported);
}

#[test]
fn behavioral_profile_refusals_are_preserved_by_analysis_controllers() {
    let mut request:Request=serde_json::from_value(serde_json::json!({
        "schema_version":"1","case_id":"behavior-profile","backend":"lu",
        "circuit":{"nodes":["0","in","out"],"elements":[
            {"kind":"voltage","id":"V1","p":1,"n":0,"volts":0.25},
            {"kind":"behavioral_voltage","id":"B1","p":2,"n":0,"cp":1,"cn":0,"expression":{"op":"input"}},
            {"kind":"resistor","id":"R1","p":2,"n":0,"ohms":1000.}
        ]},"analysis":{"kind":"operating_point"}
    })).unwrap();
    for analysis in [Analysis::OperatingPoint,Analysis::TransientConfigured{options:TransientConfig{
        stop_s:2e-4,max_step_s:1e-4,..Default::default()}}] {
        request.analysis=analysis;
        let result=api::run(&request).unwrap();
        assert!((result.samples.last().unwrap().values[1]-0.25).abs()<1e-12);
    }
    for analysis in [
        Analysis::Ac{frequencies_hz:vec![10.],source_phasors:vec![]},
        Analysis::Transient{options:circuit_simulator::transient::TransientOptions{stop_s:2e-4,step_s:1e-4,max_steps:10}},
        Analysis::TransientConfigured{options:TransientConfig{stop_s:2e-4,max_step_s:1e-4,
            method:circuit_simulator::configured::Method::Trapezoidal,..Default::default()}},
        Analysis::TransientConfigured{options:TransientConfig{stop_s:2e-4,max_step_s:1e-4,
            initial_state:Some(vec![0.25,0.25,0.,-0.00025]),..Default::default()}}
    ] {
        request.analysis=analysis;
        assert_eq!(api::run(&request).unwrap_err().code,ErrorCode::Unsupported);
    }
    request.circuit.elements.push(circuit_simulator::circuit::Element::Capacitor{
        id:"C1".into(),p:circuit_simulator::ids::NodeId(2),n:circuit_simulator::ids::NodeId(0),farads:1e-6});
    request.analysis=Analysis::TransientConfigured{options:TransientConfig{stop_s:2e-4,max_step_s:1e-4,..Default::default()}};
    assert_eq!(api::run(&request).unwrap_err().code,ErrorCode::Unsupported);
}
