//! Independent equation/unit/domain contracts for the bounded charge profile.
use circuit_simulator::charge::constant_capacitance;

#[test]
fn original_constant_charge_oracle_import_has_declared_temperature_and_transfer() {
    use circuit_simulator::{
        ac::ac_with_source_phasors, api::Analysis, backend_faer::FaerSparseLu, compile::compile,
        netlist::import_ngspice_request,
    };
    use num_complex::Complex64;
    let request = import_ngspice_request(
        include_str!("../compat/ngspice/qualification-diode-constant-charge.cir"),
        "charge-oracle",
    )
    .unwrap();
    let Analysis::Ac {
        frequencies_hz,
        source_phasors,
    } = request.analysis
    else {
        panic!("Expected AC")
    };
    let plan = compile(&request.circuit).unwrap();
    let vt = 8.617_333_262_145e-5 * 300.15;
    for sample in
        ac_with_source_phasors(&plan, &frequencies_hz, &source_phasors, &FaerSparseLu).unwrap()
    {
        let expected = Complex64::new(1., 0.5)
            / Complex64::new(
                1. + 1000. * 1e-6 / vt,
                std::f64::consts::TAU * sample.frequency_hz * 0.001,
            );
        let actual = &sample.values[1];
        assert!((Complex64::new(actual.real, actual.imag) - expected).norm() < 1e-11);
    }
}

#[test]
fn independent_composite_audits_detect_missing_displacement_and_wrong_capacitance() {
    use circuit_simulator::{
        audit::{self, AuditOptions, CheckStatus},
        circuit::Element,
    };
    for sign in [1., -1.] {
        let mut request = profile_request(serde_json::json!({"kind":"operating_point"}), "lu");
        if sign < 0. {
            if let Element::DiodeWithCapacitance { p, n, .. } = &mut request.circuit.elements[1] {
                std::mem::swap(p, n);
            }
        }
        let voltage = sign * 0.1;
        let conduction = 1e-12 * (voltage / 0.025_f64).exp_m1();
        let dc = [0.1, -sign * conduction];
        assert!(
            !audit::dc(&request.circuit, &dc, AuditOptions::default(), None)
                .unwrap()
                .has_failures()
        );
        let displacement = 1e-6 * voltage / 0.1;
        let accepted = [0.1, -sign * (conduction + displacement)];
        let report = audit::backward_euler_step(
            &request.circuit,
            &[0., 0.],
            &accepted,
            0.1,
            AuditOptions::default(),
        )
        .unwrap();
        assert!(!report.has_failures());
        let energy = report
            .checks
            .iter()
            .find(|check| check.id == "be_energy_balance")
            .unwrap();
        assert_eq!(energy.status, CheckStatus::Passed);
        assert!(energy.discrepancy.unwrap().abs() < 1e-20);
        let missing = audit::backward_euler_step(
            &request.circuit,
            &[0., 0.],
            &dc,
            0.1,
            AuditOptions::default(),
        )
        .unwrap();
        assert!(
            missing
                .checks
                .iter()
                .any(|check| check.id.starts_with("kcl:") && check.status == CheckStatus::Failed)
        );
        assert_eq!(
            missing
                .checks
                .iter()
                .find(|check| check.id == "be_energy_balance")
                .unwrap()
                .status,
            CheckStatus::Failed
        );
        if let Element::DiodeWithCapacitance { farads, .. } = &mut request.circuit.elements[1] {
            *farads = 2e-6;
        }
        assert!(
            audit::backward_euler_step(
                &request.circuit,
                &[0., 0.],
                &accepted,
                0.1,
                AuditOptions::default()
            )
            .unwrap()
            .has_failures()
        );
    }
}

#[test]
fn explicit_capacitance_variation_preserves_conduction_and_immutable_source() {
    use circuit_simulator::{
        compile::compile,
        variation::{
            ScalarKind, diode_capacitance_selection, replace_diode_capacitance, replace_scalar,
            scalar_kind,
        },
    };
    let request = profile_request(serde_json::json!({"kind":"operating_point"}), "lu");
    let original = serde_json::to_string(&request.circuit).unwrap();
    let selected = diode_capacitance_selection(&request.circuit, "D1").unwrap();
    assert_eq!(selected.kind, ScalarKind::DiodeCapacitance);
    assert_eq!(selected.unit, "F");
    assert_eq!(selected.value, 1e-6);
    assert_eq!(
        scalar_kind(&request.circuit, "D1").unwrap(),
        ScalarKind::DiodeSaturationCurrent
    );
    let varied = replace_diode_capacitance(&request.circuit, "D1", 2e-6).unwrap();
    assert_eq!(serde_json::to_string(&request.circuit).unwrap(), original);
    assert_eq!(compile(&varied).unwrap().charge_sparse().values(), &[2e-6]);
    assert_eq!(
        compile(&varied).unwrap().conductance_reference().values,
        compile(&request.circuit)
            .unwrap()
            .conductance_reference()
            .values
    );
    let varied = replace_scalar(&varied, "D1", 2e-12).unwrap();
    assert_eq!(
        diode_capacitance_selection(&varied, "D1").unwrap().value,
        2e-6
    );
    for value in [0., -1., f64::NAN, f64::INFINITY] {
        assert!(replace_diode_capacitance(&request.circuit, "D1", value).is_err());
    }
    for id in ["missing", "V1", "d1"] {
        assert!(replace_diode_capacitance(&request.circuit, id, 1e-6).is_err());
    }
}

#[test]
fn bounded_named_charge_profile_requires_explicit_zero_grading_and_supported_terms() {
    use circuit_simulator::{
        circuit::Element,
        netlist::{parse, parse_ngspice_basic},
    };
    let basic = parse("title\nD1 out 0 1p 25m 2n\nR1 out 0 1k\n.end").unwrap();
    assert!(
        matches!(basic.elements[0],Element::DiodeWithCapacitance { farads, .. } if farads==2e-9)
    );
    for card in [
        ".model DM D (IS=1p N=2 CJO=2n M=0)",
        ".model DM D(Is=1p,n=2,cj0=2n,m=0,rs=0,tt=0)",
    ] {
        let circuit = parse_ngspice_basic(&format!(
            "title\n.temp 26.85\n{card}\nD1 out 0 DM\nR1 out 0 1k\n.end"
        ))
        .unwrap();
        let Element::DiodeWithCapacitance {
            saturation_amps,
            thermal_volts,
            farads,
            ..
        } = circuit.elements[0]
        else {
            panic!("Expected profile")
        };
        assert_eq!(saturation_amps, 1e-12);
        assert_eq!(farads, 2e-9);
        assert!((thermal_volts - 2. * 8.617_333_262_145e-5 * 300.).abs() < 1e-15);
    }
    for parameters in [
        "CJO=1n",
        "M=0",
        "CJO=1n M=0.5",
        "CJO=-1n M=0",
        "CJO=1n M=0 TT=1n",
        "CJO=1n M=0 RS=1",
        "CJO=1n M=0 VJ=1",
        "CJO=1n CJ0=2n M=0",
        "IS=1p is=2p",
        "CJO=1n M=0 M=0",
    ] {
        assert!(
            parse_ngspice_basic(&format!(
                "title\n.model DM D({parameters})\nD1 out 0 DM\nR1 out 0 1k\n.end"
            ))
            .is_err(),
            "{parameters}"
        );
    }
    let zero = parse_ngspice_basic("title\n.model DM D(CJO=0 M=0)\nD1 out 0 DM\nR1 out 0 1k\n.end")
        .unwrap();
    assert!(matches!(zero.elements[0], Element::Diode { .. }));
}

#[test]
fn raw_deck_byte_budget_applies_before_comment_or_model_normalization() {
    use circuit_simulator::{
        error::ErrorCode,
        netlist::{import_ngspice_request, parse_ngspice_basic},
    };
    let oversized = format!("title\n*{}\n.end", "x".repeat(1_048_576));
    assert_eq!(
        parse_ngspice_basic(&oversized).unwrap_err().code,
        ErrorCode::ResourceExhausted
    );
    assert_eq!(
        import_ngspice_request(&oversized, "oversized")
            .unwrap_err()
            .code,
        ErrorCode::ResourceExhausted
    );
}

#[test]
fn composite_be_jacobian_has_independent_two_terminal_charge_derivatives() {
    use circuit_simulator::{
        circuit::{Circuit, Element},
        compile::compile,
        ids::NodeId,
        newton::linearize_backward_euler,
    };
    let circuit = Circuit {
        nodes: vec!["0".into(), "a".into(), "b".into()],
        elements: vec![
            Element::Resistor {
                id: "RA".into(),
                p: NodeId(1),
                n: NodeId(0),
                ohms: 2.,
            },
            Element::Resistor {
                id: "RB".into(),
                p: NodeId(2),
                n: NodeId(0),
                ohms: 3.,
            },
            Element::DiodeWithCapacitance {
                id: "D1".into(),
                p: NodeId(1),
                n: NodeId(2),
                saturation_amps: 1e-12,
                thermal_volts: 0.025,
                farads: 2e-6,
            },
        ],
    };
    let plan = compile(&circuit).unwrap();
    let values = [0.1, 0.02];
    let previous = [0.05, -0.01];
    let system = linearize_backward_euler(&plan, &values, &previous, 0.01, &[0., 0.]).unwrap();
    let voltage = values[0] - values[1];
    let current =
        1e-12 * (voltage / 0.025).exp_m1() + 2e-6 * (voltage - (previous[0] - previous[1])) / 0.01;
    assert!((system.residual[0] - (values[0] / 2. + current)).abs() < 1e-15);
    assert!((system.residual[1] - (values[1] / 3. - current)).abs() < 1e-15);
    let derivative = 1e-12 / 0.025 * (voltage / 0.025).exp() + 2e-6 / 0.01;
    let expected = [
        0.5 + derivative,
        -derivative,
        -derivative,
        1. / 3. + derivative,
    ];
    for (actual, expected) in system.jacobian.values.iter().zip(expected) {
        assert!((actual - expected).abs() < 1e-15);
    }
    for column in 0..2 {
        let mut plus = values;
        let mut minus = values;
        plus[column] += 1e-6;
        minus[column] -= 1e-6;
        let fp = linearize_backward_euler(&plan, &plus, &previous, 0.01, &[0., 0.])
            .unwrap()
            .residual;
        let fm = linearize_backward_euler(&plan, &minus, &previous, 0.01, &[0., 0.])
            .unwrap()
            .residual;
        for row in 0..2 {
            assert!(((fp[row] - fm[row]) / 2e-6 - expected[row * 2 + column]).abs() < 1e-10);
        }
    }
}

#[test]
fn unrepresentable_profile_charge_is_rejected_before_history_or_initial_observation() {
    use circuit_simulator::{
        backend::DenseLu,
        circuit::Element,
        compile::compile,
        configured::{self, TransientConfig},
        newton::linearize_backward_euler,
    };
    let mut request = profile_request(serde_json::json!({"kind":"operating_point"}), "lu");
    if let Element::Voltage { volts, .. } = &mut request.circuit.elements[0] {
        *volts = 2.;
    }
    if let Element::DiodeWithCapacitance { farads, .. } = &mut request.circuit.elements[1] {
        *farads = f64::MAX;
    }
    let plan = compile(&request.circuit).unwrap();
    assert!(linearize_backward_euler(&plan, &[0., 0.], &[2., 0.], 100., &[0., 2.]).is_err());
    let options = TransientConfig {
        stop_s: 0.1,
        max_step_s: 0.1,
        initial_state: Some(vec![2., 0.]),
        ..Default::default()
    };
    let mut observed = 0;
    assert!(
        configured::run_observed(&plan, &DenseLu, &options, &false.into(), |_| {
            observed += 1;
            Ok(())
        })
        .is_err()
    );
    assert_eq!(observed, 0);
}

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
