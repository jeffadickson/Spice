//! Independent boundary and numerical oracles for the October 29 shard.

#[test]
fn failed_final_continuation_stage_halves_the_remaining_physical_increment() {
    use circuit_simulator::{
        error::{ErrorCode, SimError, SimResult},
        linalg::{LinearSolver, Matrix},
        newton::{ContinuationOptions, NewtonOptions},
    };
    use std::sync::atomic::{AtomicUsize, Ordering};
    struct RejectThird(AtomicUsize);
    impl LinearSolver for RejectThird {
        fn name(&self) -> &'static str {
            "reject-third-nonzero-correction"
        }
        fn solve(&self, a: &Matrix, b: &[f64]) -> SimResult<Vec<f64>> {
            if b[0].abs() > 1e-6 && self.0.fetch_add(1, Ordering::Relaxed) == 2 {
                return Err(SimError::new(
                    ErrorCode::NumericalFailure,
                    "injected final-stage failure",
                ));
            }
            DenseLu.solve(a, b)
        }
    }
    let plan = compile(&parse("title\nI1 0 x 1\nR1 x 0 1").unwrap()).unwrap();
    let options = NewtonOptions {
        continuation: Some(ContinuationOptions {
            initial_step: 0.25,
            ..Default::default()
        }),
        ..Default::default()
    };
    let (_, report) =
        circuit_simulator::newton::solve(&plan, &RejectThird(AtomicUsize::new(0)), options)
            .unwrap();
    let stages = report.continuation.unwrap();
    assert_eq!(stages.rejected_stages, 1);
    assert!(stages.stages.iter().any(|s| s.source_scale == 0.875));
}

#[test]
fn ac_real_assembly_overflow_is_rejected_before_nonlinear_bias_work() {
    use circuit_simulator::{
        api::AcSourcePhasor,
        circuit::Element,
        error::SimResult,
        ids::NodeId,
        linalg::{LinearSolver, Matrix},
    };
    use std::sync::atomic::{AtomicUsize, Ordering};
    struct Count(AtomicUsize);
    impl LinearSolver for Count {
        fn name(&self) -> &'static str {
            "observed-bias"
        }
        fn solve(&self, a: &Matrix, b: &[f64]) -> SimResult<Vec<f64>> {
            self.0.fetch_add(1, Ordering::Relaxed);
            DenseLu.solve(a, b)
        }
    }
    let mut circuit = parse("title\nV1 x 0 0.1\nR1 x 0 1\nI1 0 x 0\nI2 0 x 0").unwrap();
    circuit.elements.push(Element::Diode {
        id: "D1".into(),
        p: NodeId(1),
        n: NodeId(0),
        saturation_amps: 1e-12,
        thermal_volts: 0.025,
    });
    let plan = compile(&circuit).unwrap();
    let solver = Count(AtomicUsize::new(0));
    let overrides = [
        AcSourcePhasor {
            id: "I1".into(),
            real: f64::MAX,
            imag: 0.,
        },
        AcSourcePhasor {
            id: "I2".into(),
            real: f64::MAX,
            imag: 0.,
        },
    ];
    assert!(
        circuit_simulator::ac::ac_with_source_phasors(&plan, &[1.], &overrides, &solver).is_err()
    );
    assert_eq!(solver.0.load(Ordering::Relaxed), 0);
}

#[test]
fn sparse_backward_error_accepts_exact_inclusive_limit() {
    use circuit_simulator::sparse::CscMatrix;
    let epsilon = 1e-10;
    let a = (1. - epsilon) / 2.;
    // First row cancels a-a before adding epsilon; absolute scale rounds to 1.
    // Remaining rows are exact identities. This realizes the actual f64 boundary.
    assert_eq!(a + a + epsilon, 1.);

    let matrix = CscMatrix::new(
        3,
        vec![0, 1, 3, 5],
        vec![0, 0, 1, 0, 2],
        vec![a, -a, 1., epsilon, 1.],
    )
    .unwrap();
    assert_eq!(
        circuit_simulator::dc::verify_csc_solution(&matrix, &[0., 1., 1.], &[1., 1., 1.]).unwrap(),
        epsilon
    );
}

#[test]
fn artifact_invalid_schema_is_rejected_even_when_every_other_field_is_valid() {
    use sha2::{Digest, Sha256};
    let parent = std::env::temp_dir();
    assert!(parent.is_dir());
    let root = parent.join(format!("spice-oct29-marker-{}", std::process::id()));
    std::fs::create_dir(&root).unwrap();
    let bytes = br#"{"schema_version":"1","status":"succeeded","complete":true}"#;
    std::fs::write(root.join("result.json"), bytes).unwrap();
    let marker = serde_json::json!({"schema_version":"2","complete":true,"path":"result.json","bytes":bytes.len(),"sha256":format!("{:x}",Sha256::digest(bytes))});
    std::fs::write(
        root.join("manifest.json"),
        serde_json::to_vec(&marker).unwrap(),
    )
    .unwrap();
    let bad_schema = circuit_simulator::artifact::read_verified(&root).is_err();
    let mut bad_path = marker.clone();
    bad_path["schema_version"] = serde_json::json!("1");
    bad_path["path"] = serde_json::json!("other.json");
    std::fs::write(
        root.join("manifest.json"),
        serde_json::to_vec(&bad_path).unwrap(),
    )
    .unwrap();
    let rejected_path = circuit_simulator::artifact::read_verified(&root).is_err();
    std::fs::remove_dir_all(&root).unwrap();
    assert!(bad_schema && rejected_path);
}

#[test]
fn sweep_summary_counts_indeterminate_separately() {
    use circuit_simulator::{
        assertion::Outcome,
        experiment::AssertionResult,
        sweep::{SweepPoint, assertion_summary},
        variation::{ScalarKind, ScalarSelection},
    };
    let request = serde_json::from_str(include_str!("../examples/divider.json")).unwrap();
    let mut manifest = circuit_simulator::api::run(&request).unwrap();
    manifest.assertions.push(AssertionResult {
        id: "unknown".into(),
        measurement: "missing".into(),
        outcome: Outcome::Indeterminate,
        reason: Some("missing data".into()),
    });
    manifest.assertions.push(AssertionResult {
        id: "failed".into(),
        measurement: "m".into(),
        outcome: Outcome::Fail,
        reason: Some("outside bound".into()),
    });
    let points = vec![SweepPoint {
        index: 0,
        selection: ScalarSelection {
            element_id: "R1".into(),
            kind: ScalarKind::Resistance,
            unit: "ohm",
            value: 1.,
        },
        execution: "succeeded",
        result: Some(manifest),
        error: None,
    }];
    let summary = assertion_summary(&points, "unknown");
    assert_eq!(summary.indeterminate, 1);
    assert_eq!(summary.pass + summary.fail + summary.missing, 0);
    let failures = assertion_summary(&points, "failed");
    assert_eq!(failures.fail, 1);
    assert_eq!(failures.pass, 0);
}

struct WrongPreparedIdentity;
impl circuit_simulator::linalg::LinearSolver for WrongPreparedIdentity {
    fn name(&self) -> &'static str {
        "wrong-prepared-identity"
    }
    fn solve(
        &self,
        a: &circuit_simulator::linalg::Matrix,
        b: &[f64],
    ) -> circuit_simulator::error::SimResult<Vec<f64>> {
        DenseLu.solve(a, b)
    }
    fn prepare(
        &self,
        a: &circuit_simulator::linalg::Matrix,
    ) -> circuit_simulator::error::SimResult<Option<circuit_simulator::linalg::PreparedReal>> {
        let correct = a.clone();
        let mut wrong = a.clone();
        wrong.values[0] += 1.;
        Ok(Some(circuit_simulator::linalg::PreparedReal::new(
            wrong,
            move |b| DenseLu.solve(&correct, b),
        )?))
    }
}

#[test]
fn prepared_factors_with_same_shape_but_wrong_coefficients_are_rejected() {
    let plan = compile(&parse("title\nI1 0 x 1\nR1 x 0 1\nC1 x 0 1").unwrap()).unwrap();
    let config = circuit_simulator::configured::TransientConfig {
        stop_s: 0.1,
        max_step_s: 0.1,
        initial_state: Some(vec![0.]),
        ..Default::default()
    };
    let err = circuit_simulator::configured::run(
        &plan,
        &WrongPreparedIdentity,
        &config,
        &std::sync::atomic::AtomicBool::new(false),
    )
    .err()
    .expect("wrong identity must fail");
    assert_eq!(
        err.code,
        circuit_simulator::error::ErrorCode::NumericalFailure
    );
}
use circuit_simulator::{
    audit::{self, AuditReport, Check, CheckStatus},
    backend::DenseLu,
    compile::compile,
    experiment::{ExperimentSpec, MeasurementSpec, Operation},
    expression::Expr,
    netlist::parse,
    transient::Sample,
    waveform::Waveform,
};

#[test]
fn expression_sum_derivative_and_log_domain_have_independent_oracles() {
    // A left-nested product must consume the same depth budget as other operators.
    let mut nested = Expr::Constant(1.);
    for _ in 0..64 {
        nested = Expr::Multiply(Box::new(nested), Box::new(Expr::Constant(1.)));
    }
    assert!(nested.evaluate(0.).is_ok());
    nested = Expr::Multiply(Box::new(nested), Box::new(Expr::Constant(1.)));
    assert!(nested.evaluate(0.).is_err());
    let x = || Box::new(Expr::Input);
    // d(x+x)/dx=2, distinct from the product rule at x=3.
    assert_eq!(Expr::Add(x(), x()).evaluate(3.).unwrap(), (6., 2.));
    assert!(Expr::Log(x()).evaluate(-1.).is_err());
    let (v, d) = Expr::Log(x()).evaluate(2.).unwrap();
    assert!((v - 2_f64.ln()).abs() < 1e-15 && (d - 0.5).abs() < 1e-15);
}

#[test]
fn audit_rejects_invalid_bounds_and_keeps_largest_normalized_failure() {
    assert!(Check::measured("bad", 0., -1., "V").reason.is_some());
    assert_eq!(
        Check::measured("bad", 0., -1., "V").status,
        CheckStatus::Failed
    );
    let mut report = AuditReport {
        checks: vec![Check::measured("same", 2., 1., "V")],
    };
    report.merge_worst(AuditReport {
        checks: vec![Check::measured("same", 4., 1., "V")],
    });
    assert_eq!(report.checks[0].discrepancy, Some(4.));
}

#[test]
fn refinement_bound_scales_by_magnitude_not_reciprocal() {
    let sample = |t, v| Sample {
        time_s: t,
        values: vec![v],
    };
    let coarse = vec![sample(0., 2.), sample(1., 2.)];
    let fine = vec![sample(0., 2.1), sample(0.5, 2.1), sample(1., 2.1)];
    let check = audit::compare_refinement(&coarse, &fine, 0, 0.01, 0.1, "V").unwrap();
    assert!((check.tolerance.unwrap() - 0.22).abs() < 1e-15);
    assert_eq!(check.status, CheckStatus::Passed);
}

#[test]
fn dense_backward_error_reports_known_scale_for_nonunit_solution() {
    let mut a = circuit_simulator::linalg::Matrix::zeros(1).unwrap();
    a.values[0] = 2.;
    // Residual 1e-10, scale approximately 8, accepted under 1e-10.
    let error = circuit_simulator::dc::verify_solution(&a, &[4. + 1e-10], &[2.]).unwrap();
    let expected = ((4. + 1e-10_f64) - 4.).abs() / (8. + 1e-10);
    assert!((error - expected).abs() < 1e-20);
}

#[test]
fn exact_measurement_signal_limit_and_oversized_sweep_are_independent() {
    let mut id_bound = circuit_simulator::variation::ScalarSweep {
        element_id: "r".repeat(256),
        values: vec![1.],
    };
    assert!(id_bound.validate().is_ok());
    id_bound.element_id.push('x');
    assert!(id_bound.validate().is_err());
    let mut spec = ExperimentSpec {
        measurements: vec![MeasurementSpec {
            id: "m".into(),
            signal: "x".repeat(512),
            operation: Operation::ValueAt { time_s: 0. },
        }],
        assertions: vec![],
    };
    assert!(spec.validate().is_ok());
    spec.measurements[0].signal.push('x');
    assert!(spec.validate().is_err());
    let mut sweep = circuit_simulator::variation::ScalarSweep {
        element_id: "R1".into(),
        values: vec![1.; 64],
    };
    assert!(sweep.validate().is_ok());
    sweep.values.resize(66, 1.);
    assert!(sweep.validate().is_err());
    let circuit = parse("title\nR1 x 0 1").unwrap();
    assert!(circuit_simulator::variation::replace_scalar(&circuit, "R1", f64::NAN).is_err());
}

#[test]
fn parser_resource_cap_cannot_be_bypassed_by_going_past_it() {
    let mut nodes = String::from("title\n");
    for i in 1..128 {
        nodes.push_str(&format!("R{i} n{i} 0 1\n"));
    }
    assert_eq!(parse(&nodes).unwrap().nodes.len(), 128);
    let mut exact = String::from("title\n");
    for i in 0..1024 {
        exact.push_str(&format!("R{i} x 0 1\n"));
    }
    assert!(parse(&exact).is_ok());
    let mut deck = String::from("title\n");
    for i in 0..1026 {
        deck.push_str(&format!("R{i} x 0 1\n"));
    }
    assert!(parse(&deck).is_err());
    // A legitimate resistor cannot become an exponential source by token shape.
    assert!(
        circuit_simulator::netlist::parse_ngspice_basic("title\nR1 x 0 EXP 1 2 0 1 2 1\n.end")
            .is_err()
    );
}

#[test]
fn waveform_domains_and_exact_pulse_period_are_observable() {
    let zero_frequency = Waveform::DampedSine {
        offset: 0.,
        amplitude: 1.,
        frequency_hz: 0.,
        delay_s: 0.,
        damping_per_s: 0.,
        phase_rad: 0.,
    };
    assert!(zero_frequency.validate().is_err());
    let invalid_amplitude = Waveform::DampedSine {
        offset: 0.,
        amplitude: f64::NAN,
        frequency_hz: 1.,
        delay_s: 0.,
        damping_per_s: 0.,
        phase_rad: 0.,
    };
    assert!(invalid_amplitude.validate().is_err());
    let sine = |delay| Waveform::DampedSine {
        offset: 0.,
        amplitude: 1.,
        frequency_hz: 1.,
        delay_s: delay,
        damping_per_s: 0.,
        phase_rad: 0.,
    };
    assert!(sine(f64::NAN).validate().is_err());
    assert!(sine(0.).at(-0.1).is_err());
    let pulse = Waveform::Pulse {
        low: 0.,
        high: 2.,
        delay_s: 0.,
        rise_s: 1.,
        width_s: 1.,
        fall_s: 1.,
        period_s: 3.,
    };
    assert!(pulse.validate().is_ok());
    assert_eq!(pulse.at(0.5).unwrap(), 1.);
}

#[test]
fn zero_residual_initial_guess_needs_no_correction() {
    let plan = compile(&parse("title\nI1 0 x 1\nR1 x 0 1").unwrap()).unwrap();
    let (x, report) =
        circuit_simulator::newton::solve_from(&plan, &DenseLu, Default::default(), vec![1.])
            .unwrap();
    assert_eq!(x, vec![1.]);
    assert_eq!(report.iterations, 0);
    assert_eq!(report.linear_solves, 0);
    assert_eq!(report.update_inf, 0.);
}

#[test]
fn source_continuation_stages_finish_at_full_scale_without_duplicates() {
    let plan = compile(&parse("title\nI1 0 x 1\nR1 x 0 1").unwrap()).unwrap();
    let options = circuit_simulator::newton::NewtonOptions {
        continuation: Some(Default::default()),
        ..Default::default()
    };
    let (_, report) = circuit_simulator::newton::solve(&plan, &DenseLu, options).unwrap();
    let stages = report.continuation.unwrap();
    assert_eq!(stages.stages.last().unwrap().source_scale, 1.);
    assert!(
        stages
            .stages
            .windows(2)
            .all(|w| w[1].source_scale > w[0].source_scale)
    );
}
