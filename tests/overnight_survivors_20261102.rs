//! Independent boundary and physical oracles for the November 2 Rust cohort.
use circuit_simulator::{
    ac::{ac_with_source_phasors, verify_complex_system},
    adaptive::AdaptiveOptions,
    api::{self, AcSourcePhasor, Analysis, Request},
    audit::{self, AuditOptions, AuditReport, Check, CheckStatus},
    backend::DenseLu,
    compile::compile,
    configured::{self, TransientConfig},
    error::ErrorCode,
    experiment::{self, AssertionSpec, ExperimentSpec, MeasurementSpec, Operation},
    expression::Expr,
    grid::log_grid,
    measurement::value_at,
    netlist::{parse, parse_ngspice_basic},
    newton::NewtonOptions,
    solver_contract::SparseComplexSystem,
    sources::BoundSources,
    sparse::CscMatrix,
    transient::{Sample, TransientOptions},
};
use num_complex::Complex64;

#[test]
fn ac_invalid_real_amplitude_is_rejected() {
    let plan = compile(&parse("AC\nV1 a 0 1\nR1 a 0 1").unwrap()).unwrap();
    for real in [f64::NAN, f64::INFINITY, f64::NEG_INFINITY] {
        let error = ac_with_source_phasors(
            &plan,
            &[1.],
            &[AcSourcePhasor {
                id: "V1".into(),
                real,
                imag: 0.,
            }],
            &DenseLu,
        )
        .unwrap_err();
        assert_eq!(error.code, ErrorCode::InvalidInput);
    }
}

#[test]
fn ac_backward_error_accepts_the_exact_inclusive_limit() {
    // |(2e-10+i)-i| / (|2e-10+i|+|i|) = 1e-10 in f64:
    // both norms round to exactly 1. No subtraction of nearby real values.
    let real = CscMatrix::new(1, vec![0, 1], vec![0], vec![2e-10]).unwrap();
    let imag = CscMatrix::new(1, vec![0, 1], vec![0], vec![1.]).unwrap();
    let system = SparseComplexSystem::from_real_parts(&real, &imag, 1.).unwrap();
    let rhs = [Complex64::new(0., 1.)];
    assert_eq!(
        verify_complex_system(&system, &rhs, &[Complex64::new(1., 0.)]).unwrap(),
        1e-10
    );
    assert!(verify_complex_system(&system, &rhs, &[Complex64::new(1.000001, 0.)]).is_err());
}

#[test]
fn legacy_be_audit_uses_each_actual_step_duration() {
    let request = Request {
        schema_version: "1".into(),
        case_id: "be-step-audit".into(),
        backend: "lu".into(),
        circuit: parse("RC\nV1 in 0 1\nR1 in out 1\nC1 out 0 1").unwrap(),
        analysis: Analysis::Transient {
            options: TransientOptions {
                step_s: 0.1,
                stop_s: 0.35,
                max_steps: 10,
            },
        },
        experiment: None,
    };
    let result = api::run(&request).unwrap();
    assert!(result.samples.len() >= 3);
    assert!(!result.consistency.checks.is_empty());
    assert!(
        result
            .consistency
            .checks
            .iter()
            .filter(|c| c.id != "global_accuracy")
            .all(|c| c.status == CheckStatus::Passed),
        "{:?}",
        result.consistency
    );
}

#[test]
fn vcvs_audit_checks_the_control_voltage_equation() {
    let circuit = parse("E\nV1 in 0 2\nE1 out 0 in 0 3\nR1 out 0 1").unwrap();
    // Node voltages 2 V and 5 V violate the ideal gain of 3 by -1 V.
    let report = audit::dc(&circuit, &[2., 5., 0., -5.], AuditOptions::default(), None).unwrap();
    let check = report
        .checks
        .iter()
        .find(|c| c.id == "constraint:E1")
        .unwrap();
    assert_eq!(check.status, CheckStatus::Failed);
    assert_eq!(check.discrepancy, Some(-1.));
}

#[test]
fn available_audit_evidence_replaces_an_unavailable_observation() {
    let mut report = AuditReport {
        checks: vec![Check::not_checked("x", "V", "no evidence")],
    };
    report.merge_worst(AuditReport {
        checks: vec![Check::measured("x", 0.25, 1., "V")],
    });
    assert_eq!(report.checks[0].discrepancy, Some(0.25));
    report.merge_worst(AuditReport {
        checks: vec![Check::not_checked("x", "V", "later missing")],
    });
    assert_eq!(report.checks[0].discrepancy, Some(0.25));
}

#[test]
fn adaptive_minimum_step_rejection_is_relative_at_small_time_scales() {
    let plan = compile(&parse("RC\nR1 a 0 1\nC1 a 0 1n").unwrap()).unwrap();
    let config = TransientConfig {
        stop_s: 1e-9,
        max_step_s: 1e-9,
        initial_state: Some(vec![1.]),
        adaptive: Some(AdaptiveOptions {
            relative: 1e-12,
            voltage_absolute: 1e-15,
            current_absolute: 1e-15,
            min_step_s: 1e-9,
            max_attempts: 10,
        }),
        ..Default::default()
    };
    let error = configured::run(&plan, &DenseLu, &config, &false.into())
        .err()
        .unwrap();
    assert_eq!(error.code, ErrorCode::NumericalFailure);
    assert!(error.message.contains("minimum step"));
}

fn experiment() -> ExperimentSpec {
    ExperimentSpec {
        measurements: vec![MeasurementSpec {
            id: "m".into(),
            signal: "v(a)".into(),
            operation: Operation::ValueAt { time_s: 0. },
        }],
        assertions: vec![AssertionSpec {
            id: "a".into(),
            measurement: "m".into(),
            unit: "V".into(),
            low: 0.,
            high: 1.,
        }],
    }
}

#[test]
fn experiment_measurement_time_operands_are_independently_validated() {
    for time in [-1., f64::NAN, f64::INFINITY, f64::NEG_INFINITY] {
        let mut spec = experiment();
        spec.measurements[0].operation = Operation::ValueAt { time_s: time };
        assert_eq!(spec.validate().unwrap_err().code, ErrorCode::InvalidInput);
    }
}

#[test]
fn experiment_assertion_reference_and_lower_bound_are_independently_validated() {
    let mut missing = experiment();
    missing.assertions[0].measurement = "absent".into();
    assert_eq!(
        missing.validate().unwrap_err().code,
        ErrorCode::InvalidInput
    );
    for low in [f64::NAN, f64::NEG_INFINITY] {
        let mut spec = experiment();
        spec.assertions[0].low = low;
        assert_eq!(spec.validate().unwrap_err().code, ErrorCode::InvalidInput);
    }
}

#[test]
fn window_integral_includes_interior_knots_of_a_piecewise_linear_signal() {
    let mut request: Request =
        serde_json::from_str(include_str!("../examples/divider.json")).unwrap();
    request.experiment = None;
    let mut manifest = api::run(&request).unwrap();
    manifest.samples = vec![
        Sample {
            time_s: 0.,
            values: vec![0.; manifest.signals.len()],
        },
        Sample {
            time_s: 1.,
            values: vec![2.; manifest.signals.len()],
        },
        Sample {
            time_s: 2.,
            values: vec![0.; manifest.signals.len()],
        },
    ];
    let spec = ExperimentSpec {
        measurements: vec![MeasurementSpec {
            id: "area".into(),
            signal: manifest.signals[0].id.clone(),
            operation: Operation::Integral {
                start_s: 0.,
                stop_s: 2.,
            },
        }],
        assertions: vec![],
    };
    assert_eq!(
        experiment::evaluate(&manifest, &spec).unwrap().0[0].value,
        Some(2.)
    );
}

#[test]
fn expression_node_budget_is_independent_of_the_depth_budget() {
    let mut tree = Expr::Constant(1.);
    // A full tree with 1023 nodes has depth 9 and must remain accepted.
    for _ in 0..9 {
        tree = Expr::Add(Box::new(tree.clone()), Box::new(tree));
    }
    assert_eq!(tree.evaluate(0.).unwrap(), (512., 0.));
    let too_large = Expr::Add(Box::new(tree.clone()), Box::new(tree));
    assert_eq!(
        too_large.evaluate(0.).unwrap_err().code,
        ErrorCode::InvalidInput
    );
}

#[test]
fn equal_log_grid_endpoints_are_a_valid_constant_grid() {
    assert_eq!(log_grid(1., 1., 3).unwrap(), vec![1., 1., 1.]);
}

#[test]
fn interpolation_uses_the_actual_bracketing_pair() {
    let samples = [
        Sample {
            time_s: 0.,
            values: vec![0.],
        },
        Sample {
            time_s: 1.,
            values: vec![1.],
        },
        Sample {
            time_s: 2.,
            values: vec![5.],
        },
    ];
    assert_eq!(value_at(&samples, 0, 1.5).unwrap(), 3.);
}

#[test]
fn pwl_dc_initialization_uses_the_first_knot_value() {
    let circuit = parse_ngspice_basic("* PWL\nV1 a 0 PWL 0 2 1 4\nR1 a 0 1\n.end").unwrap();
    let plan = compile(&circuit).unwrap();
    let state = circuit_simulator::dc::operating_point(&plan, &DenseLu).unwrap();
    assert_eq!(state[0], 2.);
}

#[test]
fn imported_transient_keeps_the_requested_time_grid_instead_of_defaults() {
    // Also challenges the two field deletions surfaced by the native inventory
    // quirk. Their outcomes are supplemental to the original 17-mutant cohort.
    let request = circuit_simulator::netlist::import_ngspice_request(
        "Time grid\nR1 a 0 1\nC1 a 0 1\n.tran 0.02 0.08\n.end",
        "retained-time-grid",
    )
    .unwrap();
    let Analysis::TransientConfigured { options } = request.analysis else {
        panic!("expected configured transient");
    };
    assert_eq!(options.max_step_s, 0.02);
    assert_eq!(options.stop_s, 0.08);
}

#[test]
fn omitted_newton_current_tolerance_has_its_documented_nonzero_default() {
    let mut json = serde_json::to_value(NewtonOptions::default()).unwrap();
    json.as_object_mut()
        .unwrap()
        .remove("current_absolute_tolerance");
    let options: NewtonOptions = serde_json::from_value(json).unwrap();
    assert_eq!(options.current_absolute_tolerance, 1e-12);
    options.validate().unwrap();
}

#[test]
fn nominal_sources_reject_negative_time_without_a_waveform_override() {
    let plan = compile(&parse("R\nR1 a 0 1").unwrap()).unwrap();
    let sources = BoundSources::new(&plan, &[]).unwrap();
    assert_eq!(sources.rhs(-1.).unwrap_err().code, ErrorCode::InvalidInput);
    assert_eq!(sources.rhs(0.).unwrap(), vec![0.]);
}

proptest::proptest! {
    #[test]
    fn interpolation_preserves_each_piecewise_segment(
        left in -100_f64..100., middle in -100_f64..100., right in -100_f64..100.,
        numerator in 1_u32..1000,
    ) {
        let fraction = numerator as f64 / 1000.;
        let samples = [Sample { time_s: 0., values: vec![left] },
            Sample { time_s: 1., values: vec![middle] }, Sample { time_s: 2., values: vec![right] }];
        let actual = value_at(&samples, 0, 1. + fraction).unwrap();
        let expected = middle + fraction * (right - middle);
        proptest::prop_assert!((actual - expected).abs() < 1e-10);
    }
}
