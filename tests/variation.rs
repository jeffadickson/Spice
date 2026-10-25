use circuit_simulator::{
    api::Request,
    circuit::{Circuit, Element},
    ids::NodeId,
    variation::{ScalarSweep, replace_scalar},
    sweep::run_scalar,
    registry::BackendRegistry,
};

fn fixture() -> Circuit {
    Circuit {
        nodes: vec!["0".into(), "out".into()],
        elements: vec![Element::Resistor { id: "R1".into(), p: NodeId(1), n: NodeId(0), ohms: 10. }],
    }
}

#[test]
fn scalar_sweep_returns_stable_indexed_cases() {
    let request: Request = serde_json::from_str(include_str!("../examples/divider.json")).unwrap();
    let sweep = ScalarSweep { element_id: "R1".into(), values: vec![1000., 2000.] };
    let points = run_scalar(&request, &sweep, &BackendRegistry::reference()).unwrap();
    assert_eq!(points.len(), 2);
    assert_eq!(points[0].result.as_ref().unwrap().case_id, "divider--s00");
    assert_eq!(points[1].result.as_ref().unwrap().case_id, "divider--s01");
    assert!(points.iter().all(|point| point.execution == "succeeded"));
}

#[test]
fn sweep_preserves_declared_order_and_rejects_unbounded_input() {
    let sweep = ScalarSweep { element_id: "R1".into(), values: vec![20., 10.] };
    let values: Vec<_> = sweep.circuits(&fixture()).unwrap().into_iter().map(|c| match c.elements[0] { Element::Resistor { ohms, .. } => ohms, _ => unreachable!() }).collect();
    assert_eq!(values, vec![20., 10.]);
    assert!(ScalarSweep { element_id: "R1".into(), values: vec![] }.validate().is_err());
    assert!(ScalarSweep { element_id: "R1".into(), values: vec![f64::NAN] }.validate().is_err());
}

#[test]
fn replacement_is_immutable_and_validated() {
    let original = fixture();
    let varied = replace_scalar(&original, "R1", 20.).unwrap();
    assert!(matches!(original.elements[0], Element::Resistor { ohms, .. } if ohms == 10.));
    assert!(matches!(varied.elements[0], Element::Resistor { ohms, .. } if ohms == 20.));
    assert!(replace_scalar(&original, "R1", 0.).is_err());
    assert!(replace_scalar(&original, "missing", 1.).is_err());
}
