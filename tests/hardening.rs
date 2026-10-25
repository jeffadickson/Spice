use circuit_simulator::{api::{Request,Analysis,run,run_with_registry},backend::DenseLu,compile::compile,error::ErrorCode,ids::NodeId,model::{ResistorModel,CapacitorModel,TwoTerminalModel},netlist::parse,registry::BackendRegistry,transient::{transient,TransientOptions}};
#[test]fn node_labels_do_not_change_electrical_results(){
    let circuit=parse("title\nV1 in 0 10\nR1 in out 1k\nR2 out 0 1k").unwrap();
    let mut request=Request{schema_version:"1".into(),case_id:"rename".into(),circuit,analysis:Analysis::OperatingPoint,backend:"lu".into()};
    let a=run(&request).unwrap();request.circuit.nodes[1]="renamed-in".into();request.circuit.nodes[2]="renamed-out".into();
    let b=run(&request).unwrap();assert_eq!(a.samples[0].values,b.samples[0].values);assert_ne!(a.request_sha256,b.request_sha256);
}
#[test]fn uniform_resistance_scaling_preserves_voltage(){
    let a=parse("title\nV1 in 0 10\nR1 in out 1k\nR2 out 0 1k").unwrap();
    let b=parse("title\nV1 in 0 10\nR1 in out 1meg\nR2 out 0 1meg").unwrap();
    let request=|circuit|Request{schema_version:"1".into(),case_id:"scale".into(),circuit,analysis:Analysis::OperatingPoint,backend:"lu".into()};
    let x=run(&request(a)).unwrap();let y=run(&request(b)).unwrap();assert_eq!(x.samples[0].values[1],y.samples[0].values[1]);
    assert!((x.samples[0].values[2]/1000.0-y.samples[0].values[2]).abs()<1e-14);
}
#[test]fn malformed_first_device_is_not_a_title(){assert!(parse("R1 a 0\nV1 a 0 1").is_err());}
#[test]fn registry_rejects_duplicates_and_allows_custom_ids(){
    let mut registry=BackendRegistry::reference();assert!(registry.register("lu",std::sync::Arc::new(DenseLu)).is_err());
    registry.register("custom",std::sync::Arc::new(DenseLu)).unwrap();
    let circuit=parse("title\nV1 a 0 2\nR1 a 0 1k").unwrap();
    let r=Request{schema_version:"1".into(),case_id:"custom".into(),circuit,analysis:Analysis::OperatingPoint,backend:"custom".into()};
    assert_eq!(run_with_registry(&r,&registry).unwrap().samples[0].values[0],2.0);
}
#[test]fn qr_complex_capability_is_rejected(){
    let circuit=parse("title\nV1 a 0 1\nR1 a 0 1k").unwrap();
    let r=Request{schema_version:"1".into(),case_id:"qr-ac".into(),circuit,analysis:Analysis::Ac{frequencies_hz:vec![1.0]},backend:"qr".into()};
    assert_eq!(run(&r).unwrap_err().code,ErrorCode::Unsupported);
}
#[test]fn model_coefficients_and_zero_reference(){
    assert_eq!(ResistorModel(1000.0).coefficients().unwrap().conductance,0.001);
    assert_eq!(CapacitorModel(1e-6).coefficients().unwrap().capacitance,1e-6);
    assert!(CapacitorModel(0.0).coefficients().is_err());assert_eq!(NodeId(0).unknown(),None);
}
#[test]fn fractional_last_step_hits_stop_without_duplicate_axis(){
    let p=compile(&parse("title\nV1 a 0 1\nR1 a 0 1k\nC1 a 0 1u").unwrap()).unwrap();
    assert_eq!(p.unknown_count(),2);assert_eq!(p.circuit().nodes[1],"a");
    let samples=transient(&p,&DenseLu,&TransientOptions{stop_s:0.025,step_s:0.01,max_steps:5}).unwrap();
    assert_eq!(samples.last().unwrap().time_s,0.025);assert!(samples.windows(2).all(|w|w[1].time_s>w[0].time_s));
}
#[test]fn label_bounds_and_node_ids_are_validated(){
    let mut c=parse("title\nV1 a 0 1").unwrap();c.nodes[1]="a".repeat(257);assert!(compile(&c).is_err());
    c.nodes[1]="a".into();c.elements.push(circuit_simulator::circuit::Element::Resistor{id:"bad".into(),p:NodeId(999),n:NodeId(0),ohms:1.0});assert!(compile(&c).is_err());
}

