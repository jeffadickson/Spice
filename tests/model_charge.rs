//! Independent equation/unit/domain contracts for the bounded charge profile.
use circuit_simulator::charge::constant_capacitance;

fn profile_request(analysis: serde_json::Value, backend: &str) -> circuit_simulator::api::Request {
    serde_json::from_value(serde_json::json!({
        "schema_version":"1", "case_id":"constant-charge-profile", "backend":backend,
        "circuit":{"nodes":["0","out"],"elements":[
            {"kind":"voltage","id":"V1","p":1,"n":0,"volts":0.1},
            {"kind":"diode_with_capacitance","id":"D1","p":1,"n":0,
                "saturation_amps":1e-12,"thermal_volts":0.025,"farads":1e-6}
        ]}, "analysis":analysis
    }))
    .unwrap()
}

#[test]
fn explicit_profile_compiles_charge_and_preserves_dc_ac_be_equations() {
    use circuit_simulator::{api::run, compile::compile};
    use num_complex::Complex64;
    for backend in ["lu", "faer"] {
        let request = profile_request(serde_json::json!({"kind":"operating_point"}), backend);
        let plan = compile(&request.circuit).unwrap();
        assert_eq!(plan.charge_sparse().values(), &[1e-6]);
        let dc = run(&request).unwrap();
        let conduction = 1e-12 * 4_f64.exp_m1();
        assert_eq!(dc.signals[2].id, "i_conduction(D1)");
        assert!((dc.samples[0].values[1] + conduction).abs() < 1e-18);
        assert!((dc.samples[0].values[2] - conduction).abs() < 1e-18);
        assert!(dc.dc_power.unwrap().sum_absorbed_w.abs() < 1e-18);
        let ac = run(&profile_request(
            serde_json::json!({"kind":"ac","frequencies_hz":[1000.],
            "source_phasors":[{"id":"V1","real":1.,"imag":0.5}]}),
            backend,
        ))
        .unwrap();
        let voltage = Complex64::new(1., 0.5);
        let g = 1e-12 / 0.025 * 4_f64.exp();
        let expected_source = -voltage * Complex64::new(g, std::f64::consts::TAU * 1000. * 1e-6);
        let current = &ac.ac_samples[0].values[1];
        assert!((Complex64::new(current.real, current.imag) - expected_source).norm() < 1e-12);
        let current = &ac.ac_samples[0].values[2];
        assert!((Complex64::new(current.real, current.imag) - voltage * g).norm() < 1e-18);
        let be = run(&profile_request(
            serde_json::json!({"kind":"transient",
            "options":{"stop_s":0.1,"step_s":0.1,"max_steps":1}}),
            backend,
        ))
        .unwrap();
        assert!((be.samples[0].values[1] + conduction + 1e-6).abs() < 1e-15);
        assert!((be.samples[0].values[2] - conduction).abs() < 1e-18);
    }
}

#[test]
fn compiled_diode_identity_does_not_alias_parallel_identical_models() {
    use circuit_simulator::api::{Request, run};
    let request: Request = serde_json::from_value(serde_json::json!({
        "schema_version":"1","case_id":"parallel-diode-identity",
        "circuit":{"nodes":["0","out"],"elements":[
            {"kind":"voltage","id":"V1","p":1,"n":0,"volts":0.1},
            {"kind":"diode","id":"D1","p":1,"n":0,"saturation_amps":1e-12,"thermal_volts":0.025},
            {"kind":"diode","id":"D2","p":1,"n":0,"saturation_amps":1e-12,"thermal_volts":0.025}
        ]},"analysis":{"kind":"operating_point"}
    }))
    .unwrap();
    let result = run(&request).unwrap();
    assert_eq!(
        result
            .signals
            .iter()
            .map(|signal| signal.id.as_str())
            .collect::<Vec<_>>(),
        ["v(out)", "i(V1)", "i(D1)", "i(D2)"]
    );
}

#[test]
fn profile_capacitance_is_required_positive_and_legacy_wire_remains_distinct() {
    use circuit_simulator::circuit::{Circuit, Element};
    let request = profile_request(serde_json::json!({"kind":"operating_point"}), "lu");
    for value in [0., -1., f64::NAN, f64::INFINITY] {
        let mut circuit = request.circuit.clone();
        let Element::DiodeWithCapacitance { farads, .. } = &mut circuit.elements[1] else {
            panic!("Expected profile")
        };
        *farads = value;
        assert!(circuit.validate().is_err());
    }
    let mut json = serde_json::to_value(&request.circuit).unwrap();
    json["elements"][1]
        .as_object_mut()
        .unwrap()
        .remove("farads");
    assert!(serde_json::from_value::<Circuit>(json.clone()).is_err());
    json["elements"][1]["kind"] = "diode".into();
    assert!(serde_json::from_value::<Circuit>(json.clone()).is_ok());
    json["elements"][1]["farads"] = 1e-6.into();
    assert!(serde_json::from_value::<Circuit>(json).is_err());
}

#[test]
fn constant_charge_has_signed_charge_positive_derivative_and_even_energy() {
    for voltage in [-3., 0., 3.] {
        let value = constant_capacitance(2., voltage).unwrap();
        assert_eq!(value.charge_coulombs, 2. * voltage);
        assert_eq!(value.capacitance_farads, 2.);
        assert_eq!(value.energy_joules, voltage * voltage);
        let h = 1e-4;
        let derivative = (constant_capacitance(2., voltage + h)
            .unwrap()
            .charge_coulombs
            - constant_capacitance(2., voltage - h)
                .unwrap()
                .charge_coulombs)
            / (2. * h);
        assert!((derivative - value.capacitance_farads).abs() < 1e-10);
    }
}

#[test]
fn constant_charge_rejects_bad_domains_and_avoids_needless_energy_overflow() {
    for capacitance in [0., -1., f64::NAN, f64::INFINITY] {
        assert!(constant_capacitance(capacitance, 1.).is_err());
    }
    for voltage in [f64::NAN, f64::INFINITY, f64::NEG_INFINITY] {
        assert!(constant_capacitance(1., voltage).is_err());
    }
    assert!(constant_capacitance(f64::MAX, 2.).is_err());
    assert!(constant_capacitance(1., f64::MAX).is_err());
    let value = constant_capacitance(1e-300, 1e200).unwrap();
    assert!((value.charge_coulombs / 1e-100 - 1.).abs() < 1e-15);
    assert!((value.energy_joules / 5e99 - 1.).abs() < 1e-15);
}
