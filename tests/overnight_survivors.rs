//! Independent contracts prompted by the 2026-10-27 retained native survivors.
use circuit_simulator::{
    api::{self, Analysis, Request},
    backend::{DenseLu, DenseQr},
    backend_faer::FaerSparseLu,
    circuit::{Circuit, Element},
    compile::compile,
    configured::{self, TransientConfig},
    error::ErrorCode,
    linalg::{LinearSolver, Matrix},
    netlist::parse,
};

fn request(circuit: Circuit, analysis: Analysis) -> Request {
    Request {
        schema_version: "1".into(),
        case_id: "overnight-contract".into(),
        circuit,
        analysis,
        backend: "lu".into(),
        experiment: None,
    }
}

#[test]
fn nonlinear_work_accounting_matches_actual_provider_calls_including_initialization() {
    use std::sync::atomic::{AtomicUsize, Ordering};
    struct Counter(AtomicUsize);
    impl LinearSolver for Counter {
        fn name(&self) -> &'static str {
            "counted_lu"
        }
        fn solve(
            &self,
            matrix: &Matrix,
            rhs: &[f64],
        ) -> circuit_simulator::error::SimResult<Vec<f64>> {
            self.0.fetch_add(1, Ordering::Relaxed);
            DenseLu.solve(matrix, rhs)
        }
    }
    let plan =
        compile(&parse("title\nI1 0 out 100u\nR1 out 0 1k\nC1 out 0 1u\nD1 out 0 1u 25m").unwrap())
            .unwrap();
    for initial_state in [Some(vec![0.]), None] {
        let counter = Counter(AtomicUsize::new(0));
        let options = TransientConfig {
            stop_s: 0.0002,
            max_step_s: 0.0001,
            initial_state,
            adaptive: Some(circuit_simulator::adaptive::AdaptiveOptions {
                relative: 0.05,
                voltage_absolute: 1e-4,
                current_absolute: 1e-12,
                min_step_s: 1e-7,
                max_attempts: 1000,
            }),
            ..Default::default()
        };
        let result = configured::run(&plan, &counter, &options, &false.into()).unwrap();
        assert_eq!(
            result.report.linear_solves,
            counter.0.load(Ordering::Relaxed)
        );
    }
}

#[test]
fn each_request_identity_and_element_identity_bound_is_independent() {
    let base = request(
        parse("title\nV1 out 0 1\nR1 out 0 1k").unwrap(),
        Analysis::OperatingPoint,
    );
    for id in [String::new(), "x".repeat(257)] {
        let mut invalid = base.clone();
        invalid.case_id = id;
        assert_eq!(
            api::run(&invalid).unwrap_err().code,
            ErrorCode::InvalidInput
        );
    }
    for id in [String::new(), "x".repeat(257)] {
        let mut invalid = base.circuit.clone();
        if let Element::Resistor { id: target, .. } = &mut invalid.elements[1] {
            *target = id;
        }
        assert_eq!(
            invalid.validate().unwrap_err().code,
            ErrorCode::InvalidInput
        );
    }
    assert_eq!(DenseQr.name(), "nalgebra_dense_qr_reference");
    let error = circuit_simulator::error::SimError::new(ErrorCode::InvalidInput, "bad terminal");
    assert!(
        error.to_string().contains("InvalidInput") && error.to_string().contains("bad terminal")
    );
}

#[test]
fn complex_diode_current_uses_differential_bias_with_both_terminals_active() {
    let circuit = parse("title\nVP p 0 100m\nVN n 0 200m\nD1 p n 1u 25m").unwrap();
    for backend in ["lu", "faer"] {
        let mut req = request(
            circuit.clone(),
            Analysis::Ac {
                frequencies_hz: vec![10.],
                source_phasors: vec![
                    api::AcSourcePhasor {
                        id: "VP".into(),
                        real: 1.,
                        imag: 0.25,
                    },
                    api::AcSourcePhasor {
                        id: "VN".into(),
                        real: 0.25,
                        imag: 0.5,
                    },
                ],
            },
        );
        req.backend = backend.into();
        let result = api::run(&req).unwrap();
        let index = result.signals.iter().position(|s| s.id == "i(D1)").unwrap();
        let value = &result.ac_samples[0].values[index];
        let g = 1e-6 * (-4_f64).exp() / 0.025;
        assert!((value.real - 0.75 * g).abs() < 1e-15);
        assert!((value.imag + 0.25 * g).abs() < 1e-15);
    }
}

#[test]
fn floating_vccs_terminal_currents_have_equal_opposite_polarity() {
    let circuit =
        parse("title\nVP p 0 2\nVN n 0 1\nVCP cp 0 800m\nVCN cn 0 300m\nG1 p n cp cn 2").unwrap();
    for backend in ["lu", "faer"] {
        let mut req = request(circuit.clone(), Analysis::OperatingPoint);
        req.backend = backend.into();
        let result = api::run(&req).unwrap();
        for (id, expected) in [("i(VP)", -1.), ("i(VN)", 1.)] {
            let index = result.signals.iter().position(|s| s.id == id).unwrap();
            assert!((result.samples[0].values[index] - expected).abs() < 1e-12);
        }
    }
}

#[test]
fn invalid_controlled_gains_fail_with_other_parameters_valid() {
    let mut circuit = parse("title\nV1 in 0 1\nR1 out 0 1k\nG1 out 0 in 0 1").unwrap();
    if let Element::Vccs { siemens, .. } = circuit.elements.last_mut().unwrap() {
        *siemens = f64::NAN;
    }
    assert!(circuit.validate().is_err());
    let mut circuit = parse("title\nV1 in 0 1\nR1 out 0 1k\nH1 out 0 V1 1").unwrap();
    if let Element::Ccvs { ohms, .. } = circuit.elements.last_mut().unwrap() {
        *ohms = f64::INFINITY;
    }
    assert!(circuit.validate().is_err());
}

#[test]
fn fixed_policy_bounds_accept_the_limit_and_reject_either_invalid_side() {
    for count in [0, 100001] {
        assert!(
            TransientConfig {
                max_steps: count,
                ..Default::default()
            }
            .validate()
            .is_err()
        );
    }
    assert!(
        TransientConfig {
            max_steps: 100000,
            ..Default::default()
        }
        .validate()
        .is_ok()
    );
    assert!(Matrix::zeros(0).is_err());
    assert!(Matrix::zeros(257).is_err());
    assert_eq!(Matrix::zeros(256).unwrap().values.len(), 65536);
    let invalid = circuit_simulator::newton::NewtonOptions {
        relative_tolerance: 0.,
        ..Default::default()
    };
    assert!(invalid.validate().is_err());
    assert!(
        circuit_simulator::newton::ContinuationOptions {
            initial_step: 1.,
            minimum_step: 0.1,
            max_stages: 16
        }
        .validate()
        .is_ok()
    );
}

#[test]
fn dense_acceptance_uses_products_not_reciprocals_of_solution_components() {
    let matrix = Matrix {
        n: 2,
        values: vec![2., 1., -3., 4.],
    };
    // A*[2,-3]=[1,-18], including a negative state and nonsymmetric rows.
    assert!(
        circuit_simulator::dc::verify_solution(&matrix, &[1., -18.], &[2., -3.]).unwrap() < 1e-15
    );
    assert!(circuit_simulator::dc::verify_solution(&matrix, &[1., -18.], &[2., 3.]).is_err());
}

#[test]
fn refinement_retention_limit_is_inclusive_and_rejects_overshoot() {
    use circuit_simulator::{audit, transient::Sample};
    let coarse = vec![
        Sample {
            time_s: 0.,
            values: vec![1.],
        },
        Sample {
            time_s: 1.,
            values: vec![1.],
        },
    ];
    for count in [100000, 100001] {
        let fine: Vec<_> = (0..count)
            .map(|i| Sample {
                time_s: i as f64 / (count - 1) as f64,
                values: vec![1.],
            })
            .collect();
        assert_eq!(
            audit::compare_refinement(&coarse, &fine, 0, 1e-3, 1e-3, "V").is_ok(),
            count == 100000
        );
    }
}

#[test]
fn artifact_marker_bound_precedes_json_whitespace_acceptance() {
    use circuit_simulator::artifact;
    let parent = std::path::Path::new("work");
    if !parent.is_dir() {
        assert!(std::path::Path::new(".").is_dir());
        std::fs::create_dir(parent).unwrap();
    }
    let root = parent.join(format!("survivor-artifact-{}", std::process::id()));
    assert!(!root.exists());
    let req = request(
        parse("title\nV1 out 0 1\nR1 out 0 1k").unwrap(),
        Analysis::OperatingPoint,
    );
    let directory = artifact::publish(&root, "marker-bound", &api::run(&req).unwrap()).unwrap();
    let path = directory.join("manifest.json");
    let mut marker = std::fs::read(&path).unwrap();
    marker.resize(65536, b' ');
    std::fs::write(&path, &marker).unwrap();
    assert!(artifact::read_verified(&directory).is_ok());
    marker.push(b' ');
    std::fs::write(&path, &marker).unwrap();
    assert!(artifact::read_verified(&directory).is_err());
    assert_eq!(root.parent(), Some(parent));
    std::fs::remove_dir_all(root).unwrap();
}

#[test]
fn parser_stops_at_its_resource_boundary_before_later_bad_records() {
    let mut text = "title\n".to_owned();
    for i in 0..128 {
        text.push_str(&format!("R{i} n{i} 0 1k\n"));
    }
    text.push_str("Qunsupported a b c model\n");
    assert_eq!(parse(&text).unwrap_err().code, ErrorCode::ResourceExhausted);
}

#[test]
fn negative_measurement_window_is_invalid_even_when_ordered() {
    use circuit_simulator::experiment::{ExperimentSpec, MeasurementSpec, Operation};
    let spec = ExperimentSpec {
        measurements: vec![MeasurementSpec {
            id: "m".into(),
            signal: "v(out)".into(),
            operation: Operation::Integral {
                start_s: -1.,
                stop_s: 1.,
            },
        }],
        assertions: vec![],
    };
    assert!(spec.validate().is_err());
}

#[test]
fn numerator_division_recursion_cannot_bypass_depth_limit() {
    use circuit_simulator::expression::Expr;
    let mut expression = Expr::Input;
    for _ in 0..70 {
        expression = Expr::Divide(Box::new(expression), Box::new(Expr::Constant(2.)));
    }
    assert!(expression.evaluate(1.).is_err());
}

#[test]
fn pulse_falling_edge_uses_its_declared_duration_and_damping_domains_are_separate() {
    use circuit_simulator::waveform::Waveform;
    let pulse = Waveform::Pulse {
        low: 1.,
        high: 5.,
        delay_s: 0.1,
        rise_s: 0.2,
        fall_s: 0.5,
        width_s: 0.4,
        period_s: 2.,
    };
    assert!((pulse.at(0.95).unwrap() - 3.).abs() < 1e-12);
    for damping in [f64::NAN, -1., 0.] {
        let wave = Waveform::DampedSine {
            offset: 0.,
            amplitude: 1.,
            frequency_hz: 2.,
            delay_s: 0.,
            damping_per_s: damping,
            phase_rad: 0.,
        };
        assert_eq!(wave.validate().is_ok(), damping == 0.);
    }
    for (pulsed, delay) in [(f64::NAN, 0.), (1., f64::NAN)] {
        let wave = Waveform::Exponential {
            initial: 0.,
            pulsed,
            rise_delay_s: delay,
            rise_tau_s: 1.,
            fall_delay_s: 2.,
            fall_tau_s: 1.,
        };
        assert!(wave.validate().is_err());
    }
}

#[test]
fn smooth_small_bias_newton_is_not_reported_as_damped() {
    let plan = compile(&parse("title\nV1 out 0 1m\nD1 out 0 1p 25m").unwrap()).unwrap();
    let (_, report) =
        circuit_simulator::newton::solve(&plan, &DenseLu, Default::default()).unwrap();
    assert!(report.iterations > 0);
    assert_eq!(report.damped_steps, 0);
}

#[test]
fn adaptive_step_growth_matches_independent_richardson_policy() {
    let plan = compile(&parse("title\nR1 out 0 1\nC1 out 0 1").unwrap()).unwrap();
    let options = TransientConfig {
        stop_s: 1.,
        max_step_s: 0.5,
        initial_state: Some(vec![1.]),
        adaptive: Some(circuit_simulator::adaptive::AdaptiveOptions {
            relative: 0.02,
            voltage_absolute: 0.001,
            current_absolute: 0.001,
            min_step_s: 1e-6,
            max_attempts: 1000,
        }),
        ..Default::default()
    };
    for solver in [&DenseLu as &dyn LinearSolver, &FaerSparseLu] {
        let result = configured::run(&plan, solver, &options, &false.into()).unwrap();
        assert_eq!(result.samples[1].time_s, 0.25);
        // One BE step is 4/5, two half-steps are 64/81; use their weighted
        // Richardson difference to derive the next requested step independently.
        let error = (4_f64 / 5. - 64_f64 / 81.) / 0.017;
        let next = 0.25 + 0.25 * 0.9 / error.sqrt();
        assert!((result.samples[2].time_s - next).abs() < 1e-12);
    }
}

#[test]
fn configured_retention_overshoot_is_rejected_without_requiring_exact_equality() {
    // The exact solution is zero for these zero-valued clamping sources. Avoid
    // repeatedly exercising vendor triangular solves in a retention-policy test;
    // the normal independent CSC acceptance check still validates every vector.
    struct ZeroState;
    impl LinearSolver for ZeroState {
        fn name(&self) -> &'static str {
            "known_zero_state"
        }
        fn solve(&self, _: &Matrix, rhs: &[f64]) -> circuit_simulator::error::SimResult<Vec<f64>> {
            assert!(rhs.iter().all(|value| *value == 0.));
            Ok(vec![0.; rhs.len()])
        }
        fn solve_csc(
            &self,
            _: &circuit_simulator::sparse::CscMatrix,
            rhs: &[f64],
        ) -> circuit_simulator::error::SimResult<Vec<f64>> {
            assert!(rhs.iter().all(|value| *value == 0.));
            Ok(vec![0.; rhs.len()])
        }
        fn prepare_csc(
            &self,
            matrix: &circuit_simulator::sparse::CscMatrix,
        ) -> circuit_simulator::error::SimResult<
            Option<circuit_simulator::linalg::PreparedSparseReal>,
        > {
            Ok(Some(circuit_simulator::linalg::PreparedSparseReal::new(
                matrix.clone(),
                |rhs| {
                    assert!(rhs.iter().all(|value| *value == 0.));
                    Ok(vec![0.; rhs.len()])
                },
            )))
        }
    }
    // 254 unknowns do not divide two million; equality-only guards miss this.
    let mut circuit = Circuit {
        nodes: vec!["0".into()],
        elements: vec![],
    };
    for i in 1..128 {
        circuit.nodes.push(format!("n{i}"));
        circuit.elements.push(Element::Voltage {
            id: format!("V{i}"),
            p: circuit_simulator::ids::NodeId(i),
            n: circuit_simulator::ids::NodeId(0),
            volts: 0.,
        });
    }
    let plan = compile(&circuit).unwrap();
    let options = TransientConfig {
        stop_s: 10000.,
        max_step_s: 1.,
        max_steps: 10000,
        ..Default::default()
    };
    assert_eq!(
        configured::run(&plan, &ZeroState, &options, &false.into())
            .err()
            .unwrap()
            .code,
        ErrorCode::ResourceExhausted
    );
}

#[test]
fn exact_legacy_retention_boundary_is_accepted_before_cooperative_cancellation() {
    let mut circuit = Circuit {
        nodes: vec!["0".into()],
        elements: vec![],
    };
    for i in 1..11 {
        circuit.nodes.push(format!("n{i}"));
        circuit.elements.push(Element::Voltage {
            id: format!("V{i}"),
            p: circuit_simulator::ids::NodeId(i),
            n: circuit_simulator::ids::NodeId(0),
            volts: 0.,
        });
    }
    let plan = compile(&circuit).unwrap();
    let options = circuit_simulator::transient::TransientOptions {
        stop_s: 100000.,
        step_s: 1.,
        max_steps: 100000,
    };
    assert_eq!(
        circuit_simulator::transient::transient_controlled(&plan, &DenseLu, &options, &true.into())
            .unwrap_err()
            .code,
        ErrorCode::Cancelled
    );
}

#[test]
fn exact_sweep_id_limit_is_accepted_and_overlength_is_rejected() {
    let mut req = request(
        parse("title\nV1 out 0 1\nR1 out 0 1k").unwrap(),
        Analysis::OperatingPoint,
    );
    let sweep = circuit_simulator::variation::ScalarSweep {
        element_id: "R1".into(),
        values: vec![1000.],
    };
    req.case_id = "x".repeat(220);
    assert_eq!(
        circuit_simulator::sweep::run_scalar_cancellable(
            &req,
            &sweep,
            &Default::default(),
            &true.into()
        )
        .unwrap_err()
        .code,
        ErrorCode::Cancelled
    );
    req.case_id.push('x');
    assert_eq!(
        circuit_simulator::sweep::run_scalar_cancellable(
            &req,
            &sweep,
            &Default::default(),
            &true.into()
        )
        .unwrap_err()
        .code,
        ErrorCode::InvalidInput
    );
}

#[test]
fn splitmix_stream_matches_published_reference_vectors() {
    // SplitMix64 reference: https://prng.di.unimi.it/splitmix64.c; seed zero.
    let mut sequence = circuit_simulator::sampling::SeededSequence::new(0);
    for expected in [
        0xe220_a839_7b1d_cdaf,
        0x6e78_9e6a_a1b9_65f4,
        0x06c4_5d18_8009_454f,
    ] {
        assert_eq!(sequence.next_u64(), expected);
    }
}
