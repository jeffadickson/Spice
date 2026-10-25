use circuit_simulator::{
    ac::ac,
    api::{Analysis, Request, run},
    backend::{DenseLu, DenseQr},
    circuit::{Circuit, Element},
    compile::compile,
    dc::{operating_point, operating_point_detailed},
    ids::NodeId,
    model::DiodeModel,
    netlist::parse,
    power::dc_power,
    newton::{NewtonOptions, linearize, norm_inf, solve, solve_from},
    transient::{TransientOptions, transient},
};

#[test]
fn shockley_diode_has_positive_incremental_conductance() {
    let diode = DiodeModel {
        saturation_amps: 1e-12,
        thermal_volts: 0.02585,
    };
    let zero = diode.linearize(0.).unwrap();
    let forward = diode.linearize(0.6).unwrap();
    assert!(zero.current_amps.abs() < 1e-24);
    assert!(forward.current_amps > 0.);
    assert!(forward.conductance_siemens > zero.conductance_siemens);
}

#[test]
fn dense_lu_and_qr_agree_on_diode_operating_point() {
    let circuit = Circuit { nodes: vec!["0".into(), "s".into(), "a".into()], elements: vec![
        Element::Voltage { id: "V1".into(), p: NodeId(1), n: NodeId(0), volts: 1. },
        Element::Resistor { id: "R1".into(), p: NodeId(1), n: NodeId(2), ohms: 1000. },
        Element::Diode { id: "D1".into(), p: NodeId(2), n: NodeId(0), saturation_amps: 1e-12, thermal_volts: 0.02585 },
    ] };
    let plan = compile(&circuit).unwrap();
    let lu = operating_point(&plan, &DenseLu).unwrap();
    let qr = operating_point(&plan, &DenseQr).unwrap();
    assert!((lu[1] - qr[1]).abs() < 1e-10);
}

#[test]
fn bounded_diode_parameter_corpus_produces_finite_operating_points() {
    for scale in [0.1, 1., 10.] {
        let circuit = Circuit { nodes: vec!["0".into(), "s".into(), "a".into()], elements: vec![
            Element::Voltage { id: "V1".into(), p: NodeId(1), n: NodeId(0), volts: 1. },
            Element::Resistor { id: "R1".into(), p: NodeId(1), n: NodeId(2), ohms: 1000. * scale },
            Element::Diode { id: "D1".into(), p: NodeId(2), n: NodeId(0), saturation_amps: 1e-12 * scale, thermal_volts: 0.02585 },
        ] };
        let values = operating_point(&compile(&circuit).unwrap(), &DenseLu).unwrap();
        assert!(values.iter().all(|value| value.is_finite()));
    }
}

#[test]
fn diode_dc_power_participates_in_balance_diagnostics() {
    let circuit = Circuit { nodes: vec!["0".into(), "s".into(), "a".into()], elements: vec![
        Element::Voltage { id: "V1".into(), p: NodeId(1), n: NodeId(0), volts: 1. },
        Element::Resistor { id: "R1".into(), p: NodeId(1), n: NodeId(2), ohms: 1000. },
        Element::Diode { id: "D1".into(), p: NodeId(2), n: NodeId(0), saturation_amps: 1e-12, thermal_volts: 0.02585 },
    ] };
    let plan = compile(&circuit).unwrap();
    let power = dc_power(&plan, &operating_point(&plan, &DenseLu).unwrap()).unwrap();
    assert!(power.devices.iter().any(|device| device.id == "D1" && device.absorbed_w > 0.));
    assert!(power.sum_absorbed_w.abs() < 1e-10);
}

#[test]
fn json_operating_point_manifest_exposes_newton_report_for_diode_case() {
    let circuit = Circuit {
        nodes: vec!["0".into(), "a".into()],
        elements: vec![
            Element::Voltage {
                id: "V1".into(),
                p: NodeId(1),
                n: NodeId(0),
                volts: 0.2,
            },
            Element::Diode {
                id: "D1".into(),
                p: NodeId(1),
                n: NodeId(0),
                saturation_amps: 1e-12,
                thermal_volts: 0.02585,
            },
        ],
    };
    let result = run(&Request {
        schema_version: "1".into(),
        case_id: "diode-json".into(),
        circuit,
        analysis: Analysis::OperatingPoint,
        backend: "lu".into(),
        experiment: None,
    })
    .unwrap();
    assert!(result.newton.is_some());
}

#[test]
fn nonlinear_operating_point_retains_newton_convergence_evidence() {
    let circuit = Circuit {
        nodes: vec!["0".into(), "a".into()],
        elements: vec![
            Element::Voltage {
                id: "V1".into(),
                p: NodeId(1),
                n: NodeId(0),
                volts: 0.2,
            },
            Element::Diode {
                id: "D1".into(),
                p: NodeId(1),
                n: NodeId(0),
                saturation_amps: 1e-12,
                thermal_volts: 0.02585,
            },
        ],
    };
    let (_, report) = operating_point_detailed(&compile(&circuit).unwrap(), &DenseLu).unwrap();
    assert!(report.unwrap().residual_inf < 1e-9);
}

#[test]
fn netlist_accepts_explicit_diode_parameters() {
    let circuit = parse("diode\nD1 a 0 1p 25.85m\n.end").unwrap();
    assert!(matches!(circuit.elements[0], Element::Diode { .. }));
    assert!(parse("diode\nD1 a 0 1p\n.end").is_err());
}

#[test]
fn nonlinear_transient_is_rejected_until_newton_time_stepping_exists() {
    let circuit = Circuit {
        nodes: vec!["0".into(), "a".into()],
        elements: vec![Element::Diode {
            id: "D1".into(),
            p: NodeId(1),
            n: NodeId(0),
            saturation_amps: 1e-12,
            thermal_volts: 0.02585,
        }],
    };
    assert!(
        transient(
            &compile(&circuit).unwrap(),
            &DenseLu,
            &TransientOptions {
                stop_s: 1e-3,
                step_s: 1e-4,
                max_steps: 20
            }
        )
        .is_err()
    );
}

#[test]
fn nonlinear_ac_is_explicitly_unsupported_until_bias_linearization_exists() {
    let circuit = Circuit {
        nodes: vec!["0".into(), "a".into()],
        elements: vec![Element::Diode {
            id: "D1".into(),
            p: NodeId(1),
            n: NodeId(0),
            saturation_amps: 1e-12,
            thermal_volts: 0.02585,
        }],
    };
    assert!(ac(&compile(&circuit).unwrap(), &[1.], &DenseLu).is_err());
}

#[test]
fn newton_rejects_malformed_initial_mna_states() {
    let circuit = Circuit {
        nodes: vec!["0".into(), "a".into()],
        elements: vec![Element::Resistor {
            id: "R1".into(),
            p: NodeId(1),
            n: NodeId(0),
            ohms: 1.,
        }],
    };
    assert!(
        solve_from(
            &compile(&circuit).unwrap(),
            &DenseLu,
            NewtonOptions::default(),
            vec![f64::NAN]
        )
        .is_err()
    );
}

#[test]
fn standard_operating_point_selects_newton_when_diodes_exist() {
    let circuit = Circuit {
        nodes: vec!["0".into(), "a".into()],
        elements: vec![
            Element::Voltage {
                id: "V1".into(),
                p: NodeId(1),
                n: NodeId(0),
                volts: 0.2,
            },
            Element::Diode {
                id: "D1".into(),
                p: NodeId(1),
                n: NodeId(0),
                saturation_amps: 1e-12,
                thermal_volts: 0.02585,
            },
        ],
    };
    assert!(
        (operating_point(&compile(&circuit).unwrap(), &DenseLu).unwrap()[0] - 0.2).abs() < 1e-12
    );
}

#[test]
fn forward_biased_resistor_diode_bias_point_converges() {
    let circuit = Circuit {
        nodes: vec!["0".into(), "s".into(), "a".into()],
        elements: vec![
            Element::Voltage {
                id: "V1".into(),
                p: NodeId(1),
                n: NodeId(0),
                volts: 1.,
            },
            Element::Resistor {
                id: "R1".into(),
                p: NodeId(1),
                n: NodeId(2),
                ohms: 1000.,
            },
            Element::Diode {
                id: "D1".into(),
                p: NodeId(2),
                n: NodeId(0),
                saturation_amps: 1e-12,
                thermal_volts: 0.02585,
            },
        ],
    };
    let values = operating_point(&compile(&circuit).unwrap(), &DenseLu).unwrap();
    assert!((0.45..0.65).contains(&values[1]));
    let diode_current = 1e-12 * (values[1] / 0.02585).exp_m1();
    assert!((diode_current - (1. - values[1]) / 1000.).abs() < 1e-10);
}

#[test]
fn high_bias_diode_uses_bounded_damping_instead_of_exponential_overflow() {
    let circuit = Circuit {
        nodes: vec!["0".into(), "s".into(), "a".into()],
        elements: vec![
            Element::Voltage {
                id: "V1".into(),
                p: NodeId(1),
                n: NodeId(0),
                volts: 5.,
            },
            Element::Resistor {
                id: "R1".into(),
                p: NodeId(1),
                n: NodeId(2),
                ohms: 1000.,
            },
            Element::Diode {
                id: "D1".into(),
                p: NodeId(2),
                n: NodeId(0),
                saturation_amps: 1e-12,
                thermal_volts: 0.02585,
            },
        ],
    };
    let (values, report) = solve(
        &compile(&circuit).unwrap(),
        &DenseLu,
        NewtonOptions::default(),
    )
    .unwrap();
    assert!((0.5..0.9).contains(&values[1]));
    assert!(report.damped_steps > 0);
}

#[test]
fn newton_iteration_budget_is_a_real_failure_boundary() {
    let circuit = Circuit {
        nodes: vec!["0".into(), "s".into(), "a".into()],
        elements: vec![
            Element::Voltage {
                id: "V1".into(),
                p: NodeId(1),
                n: NodeId(0),
                volts: 5.,
            },
            Element::Resistor {
                id: "R1".into(),
                p: NodeId(1),
                n: NodeId(2),
                ohms: 1000.,
            },
            Element::Diode {
                id: "D1".into(),
                p: NodeId(2),
                n: NodeId(0),
                saturation_amps: 1e-12,
                thermal_volts: 0.02585,
            },
        ],
    };
    let options = NewtonOptions {
        max_iterations: 1,
        ..NewtonOptions::default()
    };
    assert!(solve(&compile(&circuit).unwrap(), &DenseLu, options).is_err());
}

#[test]
fn zero_bias_diode_converges_without_a_spurious_current() {
    let circuit = Circuit {
        nodes: vec!["0".into(), "a".into()],
        elements: vec![
            Element::Diode {
                id: "D1".into(),
                p: NodeId(1),
                n: NodeId(0),
                saturation_amps: 1e-12,
                thermal_volts: 0.02585,
            },
            Element::Resistor {
                id: "R1".into(),
                p: NodeId(1),
                n: NodeId(0),
                ohms: 1000.,
            },
        ],
    };
    let (values, report) = solve(
        &compile(&circuit).unwrap(),
        &DenseLu,
        NewtonOptions::default(),
    )
    .unwrap();
    assert!(values[0].abs() < 1e-12);
    assert_eq!(report.iterations, 0);
}

#[test]
fn diode_residual_and_jacobian_are_assembled_analytically() {
    let circuit = Circuit {
        nodes: vec!["0".into(), "a".into()],
        elements: vec![Element::Diode {
            id: "D1".into(),
            p: NodeId(1),
            n: NodeId(0),
            saturation_amps: 1e-12,
            thermal_volts: 0.02585,
        }],
    };
    let system = linearize(&compile(&circuit).unwrap(), &[0.]).unwrap();
    assert!(norm_inf(&system.residual).unwrap() < 1e-20);
    assert!((system.jacobian.values[0] - 1e-12 / 0.02585).abs() < 1e-22);
}

#[test]
fn compilation_separates_nonlinear_devices_from_linear_mna_terms() {
    let circuit = Circuit {
        nodes: vec!["0".into(), "a".into()],
        elements: vec![Element::Diode {
            id: "D1".into(),
            p: NodeId(1),
            n: NodeId(0),
            saturation_amps: 1e-12,
            thermal_volts: 0.02585,
        }],
    };
    assert!(compile(&circuit).unwrap().has_nonlinear_devices());
}

#[test]
fn diode_circuit_parameters_are_validated_at_the_public_boundary() {
    let circuit = Circuit {
        nodes: vec!["0".into(), "a".into()],
        elements: vec![Element::Diode {
            id: "D1".into(),
            p: NodeId(1),
            n: NodeId(0),
            saturation_amps: 1e-12,
            thermal_volts: 0.02585,
        }],
    };
    assert!(circuit.validate().is_ok());
    let mut invalid = circuit.clone();
    if let Element::Diode { thermal_volts, .. } = &mut invalid.elements[0] {
        *thermal_volts = 0.;
    }
    assert!(invalid.validate().is_err());
}

#[test]
fn diode_rejects_invalid_parameters_and_overflowing_trials() {
    assert!(
        DiodeModel {
            saturation_amps: 0.,
            thermal_volts: 0.025
        }
        .linearize(0.)
        .is_err()
    );
    assert!(
        DiodeModel {
            saturation_amps: 1e-12,
            thermal_volts: 0.
        }
        .linearize(0.)
        .is_err()
    );
    assert!(
        DiodeModel {
            saturation_amps: 1e-12,
            thermal_volts: 0.025
        }
        .linearize(100.)
        .is_err()
    );
}
