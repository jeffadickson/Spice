use circuit_simulator::{backend::DenseLu, circuit::{Circuit, Element}, compile::compile, dc::operating_point, ids::NodeId};

#[test]
fn vccs_stamps_controlled_current_with_declared_polarity() {
    let circuit = Circuit { nodes: vec!["0".into(), "in".into(), "out".into()], elements: vec![
        Element::Voltage { id: "V1".into(), p: NodeId(1), n: NodeId(0), volts: 2. },
        Element::Vccs { id: "G1".into(), p: NodeId(2), n: NodeId(0), cp: NodeId(1), cn: NodeId(0), siemens: 0.5 },
        Element::Resistor { id: "R1".into(), p: NodeId(2), n: NodeId(0), ohms: 1. },
    ]};
    let x = operating_point(&compile(&circuit).unwrap(), &DenseLu).unwrap();
    assert!((x[1] + 1.).abs() < 1e-12);
}

#[test]
fn vccs_negative_control_voltage_reverses_output_polarity() {
    let circuit = Circuit { nodes: vec!["0".into(), "in".into(), "out".into()], elements: vec![
        Element::Voltage { id: "V1".into(), p: NodeId(1), n: NodeId(0), volts: -2. },
        Element::Vccs { id: "G1".into(), p: NodeId(2), n: NodeId(0), cp: NodeId(1), cn: NodeId(0), siemens: 0.5 },
        Element::Resistor { id: "R1".into(), p: NodeId(2), n: NodeId(0), ohms: 1. },
    ]};
    let x = operating_point(&compile(&circuit).unwrap(), &DenseLu).unwrap();
    assert!((x[1] - 1.).abs() < 1e-12);
}
