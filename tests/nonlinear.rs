use circuit_simulator::{circuit::{Circuit, Element}, compile::compile, ids::NodeId, model::DiodeModel, newton::{linearize, norm_inf}};

#[test]
fn shockley_diode_has_positive_incremental_conductance() {
    let diode = DiodeModel { saturation_amps: 1e-12, thermal_volts: 0.02585 };
    let zero = diode.linearize(0.).unwrap();
    let forward = diode.linearize(0.6).unwrap();
    assert!(zero.current_amps.abs() < 1e-24);
    assert!(forward.current_amps > 0.);
    assert!(forward.conductance_siemens > zero.conductance_siemens);
}

#[test]
fn diode_residual_and_jacobian_are_assembled_analytically() {
    let circuit = Circuit { nodes: vec!["0".into(), "a".into()], elements: vec![Element::Diode {
        id: "D1".into(), p: NodeId(1), n: NodeId(0), saturation_amps: 1e-12, thermal_volts: 0.02585,
    }] };
    let system = linearize(&compile(&circuit).unwrap(), &[0.]).unwrap();
    assert!(norm_inf(&system.residual).unwrap() < 1e-20);
    assert!((system.jacobian.at(0, 0) - 1e-12 / 0.02585).abs() < 1e-22);
}

#[test]
fn compilation_separates_nonlinear_devices_from_linear_mna_terms() {
    let circuit = Circuit { nodes: vec!["0".into(), "a".into()], elements: vec![Element::Diode {
        id: "D1".into(), p: NodeId(1), n: NodeId(0), saturation_amps: 1e-12, thermal_volts: 0.02585,
    }] };
    assert!(compile(&circuit).unwrap().has_nonlinear_devices());
}

#[test]
fn diode_circuit_parameters_are_validated_at_the_public_boundary() {
    let circuit = Circuit { nodes: vec!["0".into(), "a".into()], elements: vec![Element::Diode {
        id: "D1".into(), p: NodeId(1), n: NodeId(0), saturation_amps: 1e-12, thermal_volts: 0.02585,
    }] };
    assert!(circuit.validate().is_ok());
    let mut invalid = circuit.clone();
    if let Element::Diode { thermal_volts, .. } = &mut invalid.elements[0] { *thermal_volts = 0.; }
    assert!(invalid.validate().is_err());
}

#[test]
fn diode_rejects_invalid_parameters_and_overflowing_trials() {
    assert!(DiodeModel { saturation_amps: 0., thermal_volts: 0.025 }.linearize(0.).is_err());
    assert!(DiodeModel { saturation_amps: 1e-12, thermal_volts: 0. }.linearize(0.).is_err());
    assert!(DiodeModel { saturation_amps: 1e-12, thermal_volts: 0.025 }.linearize(100.).is_err());
}
