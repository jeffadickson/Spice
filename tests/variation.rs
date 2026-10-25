use circuit_simulator::{
    circuit::{Circuit, Element},
    ids::NodeId,
    variation::replace_scalar,
};

fn fixture() -> Circuit {
    Circuit {
        nodes: vec!["0".into(), "out".into()],
        elements: vec![Element::Resistor { id: "R1".into(), p: NodeId(1), n: NodeId(0), ohms: 10. }],
    }
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
