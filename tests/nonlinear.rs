use circuit_simulator::model::DiodeModel;

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
fn diode_rejects_invalid_parameters_and_overflowing_trials() {
    assert!(DiodeModel { saturation_amps: 0., thermal_volts: 0.025 }.linearize(0.).is_err());
    assert!(DiodeModel { saturation_amps: 1e-12, thermal_volts: 0. }.linearize(0.).is_err());
    assert!(DiodeModel { saturation_amps: 1e-12, thermal_volts: 0.025 }.linearize(100.).is_err());
}
