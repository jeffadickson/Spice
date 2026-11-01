//! Independent contract oracles for the fourteen November 1 native survivors.
//! These tests isolate guards so a later rejection cannot mask an earlier gap.
use circuit_simulator::{
    audit::{self, AuditOptions, CheckStatus},
    backend::DenseLu,
    backend_faer::FaerSparseLu,
    circuit::{Circuit, Element},
    compile::compile,
    configured::{self, TransientConfig},
    error::{ErrorCode, SimResult},
    expression::Expr,
    ids::NodeId,
    linalg::{LinearSolver, Matrix},
    measurement::value_at,
    netlist::parse,
    newton::NewtonOptions,
    transient::Sample,
    waveform::Waveform,
};
use sha2::{Digest, Sha256};

#[test]
fn artifact_payload_fields_are_individually_required_even_with_a_valid_digest() {
    let parent = std::env::temp_dir();
    assert!(parent.is_dir());
    let directory = parent.join(format!(
        "spice-nov1-payload-{}-{}",
        std::process::id(),
        std::time::SystemTime::now()
            .duration_since(std::time::UNIX_EPOCH)
            .unwrap()
            .as_nanos()
    ));
    std::fs::create_dir(&directory).unwrap();
    // A mutation assertion may unwind; clean only this newly created directory.
    struct Cleanup(std::path::PathBuf, std::path::PathBuf);
    impl Drop for Cleanup {
        fn drop(&mut self) {
            if let Ok(path) = self.0.canonicalize() {
                if path.parent() == Some(self.1.as_path()) {
                    let _ = std::fs::remove_dir_all(path);
                }
            }
        }
    }
    let _cleanup = Cleanup(directory.clone(), parent.canonicalize().unwrap());
    let valid = serde_json::json!({"schema_version":"1","status":"succeeded","complete":true});
    for (field, wrong) in [
        ("complete", serde_json::json!(false)),
        ("status", serde_json::json!("failed")),
        ("schema_version", serde_json::json!("2")),
    ] {
        let mut value = valid.clone();
        value[field] = wrong;
        let bytes = serde_json::to_vec(&value).unwrap();
        let marker = serde_json::json!({"schema_version":"1","complete":true,"path":"result.json",
            "bytes":bytes.len(),"sha256":format!("{:x}",Sha256::digest(&bytes))});
        std::fs::write(directory.join("result.json"), &bytes).unwrap();
        std::fs::write(
            directory.join("manifest.json"),
            serde_json::to_vec(&marker).unwrap(),
        )
        .unwrap();
        assert!(
            circuit_simulator::artifact::read_verified(&directory).is_err(),
            "{field}"
        );
    }
}

#[test]
fn dc_power_audit_reports_the_independent_signed_power_sum() {
    let circuit = parse("Power\nV1 x 0 1\nR1 x 0 1\n.end").unwrap();
    // 1 V * 1 A absorbed by R, minus 1 V * 0.5 A supplied by V1 = +0.5 W.
    let options = AuditOptions {
        power_absolute_w: 0.0625,
        relative: 0.125,
        ..Default::default()
    };
    let report = audit::dc(&circuit, &[1., -0.5], options, None).unwrap();
    let power = report
        .checks
        .iter()
        .find(|c| c.id == "power_balance")
        .unwrap();
    assert_eq!(power.status, CheckStatus::Failed);
    assert_eq!(power.discrepancy, Some(0.5));
    // Absolute powers total 1.5 W, so 1/16 W + (1/8)*1.5 W = 1/4 W.
    assert_eq!(power.tolerance, Some(0.25));
    assert_eq!(power.unit, "W");
}

#[test]
fn sparse_provider_identity_matches_its_public_capability_descriptor() {
    let solver = FaerSparseLu;
    assert_eq!(solver.name(), "faer_sparse_lu");
    assert_eq!(solver.capabilities().id, "faer_sparse_lu");
}

#[test]
fn node_and_element_resource_limits_are_independent() {
    let circuit = Circuit {
        nodes: (0..129).map(|n| n.to_string()).collect(),
        elements: vec![Element::Resistor {
            id: "R1".into(),
            p: NodeId(1),
            n: NodeId(0),
            ohms: 1.,
        }],
    };
    assert_eq!(
        circuit.validate().unwrap_err().code,
        ErrorCode::ResourceExhausted
    );
    let circuit = Circuit {
        nodes: vec!["0".into(), "x".into()],
        elements: (0..1025)
            .map(|n| Element::Resistor {
                id: format!("R{n}"),
                p: NodeId(1),
                n: NodeId(0),
                ohms: 1.,
            })
            .collect(),
    };
    assert_eq!(
        circuit.validate().unwrap_err().code,
        ErrorCode::ResourceExhausted
    );
}

#[test]
fn diode_nonfinite_parameters_are_rejected_one_at_a_time() {
    for invalid in [f64::NAN, f64::INFINITY, f64::NEG_INFINITY] {
        for (saturation_amps, thermal_volts) in [(invalid, 0.025), (1e-12, invalid)] {
            let circuit = Circuit {
                nodes: vec!["0".into(), "x".into()],
                elements: vec![Element::Diode {
                    id: "D1".into(),
                    p: NodeId(1),
                    n: NodeId(0),
                    saturation_amps,
                    thermal_volts,
                }],
            };
            assert!(circuit.validate().is_err());
        }
    }
}

#[test]
fn invalid_dynamic_initial_state_is_rejected_before_any_observer_call() {
    let circuit = parse("RC\nI1 0 x 1\nR1 x 0 1\nC1 x 0 1\n.end").unwrap();
    let plan = compile(&circuit).unwrap();
    for invalid in [f64::NAN, f64::INFINITY] {
        let config = TransientConfig {
            initial_state: Some(vec![invalid]),
            ..Default::default()
        };
        let mut observed = 0;
        let result = configured::run_observed(&plan, &DenseLu, &config, &false.into(), |_| {
            observed += 1;
            Ok(())
        });
        assert!(result.is_err());
        assert_eq!(
            observed, 0,
            "invalid accepted sample escaped the initial-state boundary"
        );
    }
}

/// Exact zero RHS has the analytically known zero solution. This bounded test
/// solver avoids unnecessary factorization while exercising real recording/grid logic.
struct ZeroRhs;
impl LinearSolver for ZeroRhs {
    fn name(&self) -> &'static str {
        "zero-rhs-contract-fixture"
    }
    fn solve(&self, matrix: &Matrix, rhs: &[f64]) -> SimResult<Vec<f64>> {
        assert_eq!(matrix.n, rhs.len());
        assert!(rhs.iter().all(|v| *v == 0.));
        Ok(vec![0.; rhs.len()])
    }
}

fn independent_resistors(count: usize) -> Circuit {
    Circuit {
        nodes: (0..=count).map(|n| n.to_string()).collect(),
        elements: (1..=count)
            .map(|n| Element::Resistor {
                id: format!("R{n}"),
                p: NodeId(n),
                n: NodeId(0),
                ohms: 1.,
            })
            .collect(),
    }
}

#[test]
fn configured_recording_accepts_exactly_two_million_scalars() {
    let plan = compile(&independent_resistors(20)).unwrap();
    let config = TransientConfig {
        stop_s: 99999.,
        max_step_s: 1.,
        max_steps: 100000,
        ..Default::default()
    };
    let result = configured::run(&plan, &ZeroRhs, &config, &false.into()).unwrap();
    assert_eq!(result.samples.len(), 100000);
    assert_eq!(result.samples.len() * plan.unknown_count(), 2_000_000);
    assert_eq!(result.samples.last().unwrap().time_s, 99999.);
}

#[test]
fn configured_nanosecond_grid_does_not_snap_early() {
    let plan = compile(&independent_resistors(1)).unwrap();
    let config = TransientConfig {
        stop_s: 3e-9,
        max_step_s: 1e-9,
        max_steps: 4,
        ..Default::default()
    };
    let result = configured::run(&plan, &ZeroRhs, &config, &false.into()).unwrap();
    assert_eq!(result.samples.len(), 4);
    for (sample, expected) in result.samples.iter().zip([0., 1e-9, 2e-9, 3e-9]) {
        assert!((sample.time_s - expected).abs() < 1e-24);
    }
}

#[test]
fn denominator_recursion_obeys_the_depth_limit_independently_of_node_budget() {
    let mut expression = Expr::Constant(1.);
    for _ in 0..70 {
        expression = Expr::Divide(Box::new(Expr::Constant(1.)), Box::new(expression));
    }
    assert!(expression.evaluate(0.).is_err());
}

#[test]
fn measurement_rejects_nonfinite_selected_values_and_duplicate_times_independently() {
    let samples = [
        Sample {
            time_s: 0.,
            values: vec![f64::NAN],
        },
        Sample {
            time_s: 1.,
            values: vec![2.],
        },
    ];
    assert!(value_at(&samples, 0, 0.).is_err());
    let samples = [
        Sample {
            time_s: 0.,
            values: vec![1.],
        },
        Sample {
            time_s: 0.,
            values: vec![2.],
        },
    ];
    assert!(value_at(&samples, 0, 0.).is_err());
}

#[test]
fn measurements_outside_either_endpoint_preserve_the_typed_error_contract() {
    let samples = [
        Sample {
            time_s: 0.,
            values: vec![1.],
        },
        Sample {
            time_s: 1.,
            values: vec![2.],
        },
    ];
    for time in [-1., 2.] {
        // Context messages are not stable API identifiers (see error.rs).
        // Both early and fallback rejection must preserve the typed contract.
        assert_eq!(
            value_at(&samples, 0, time).unwrap_err().code,
            ErrorCode::InvalidInput
        );
    }
    assert_eq!(value_at(&samples, 0, 0.).unwrap(), 1.);
    assert_eq!(value_at(&samples, 0, 1.).unwrap(), 2.);
}
#[test]
fn gmin_accepts_the_inclusive_upper_limit_and_rejects_larger_values() {
    assert!(NewtonOptions {
        gmin_siemens: Some(1.),
        ..Default::default()
    }
    .validate()
    .is_ok());
    assert!(NewtonOptions {
        gmin_siemens: Some(1.0001),
        ..Default::default()
    }
    .validate()
    .is_err());
}

#[test]
fn constant_source_validation_rejects_every_nonfinite_value() {
    for value in [f64::NAN, f64::INFINITY, f64::NEG_INFINITY] {
        assert!(Waveform::Constant { value }.validate().is_err());
    }
    assert!(Waveform::Constant { value: 0. }.validate().is_ok());
}

#[test]
fn pulse_delay_rise_and_fall_fields_are_validated_independently() {
    for (delay_s, rise_s, fall_s) in [
        (-1., 0., 0.),
        (0., f64::NAN, 0.),
        (0., f64::INFINITY, 0.),
        (0., -1., 0.),
        (0., 0., f64::NAN),
        (0., 0., f64::INFINITY),
    ] {
        assert!(Waveform::Pulse {
            low: 0.,
            high: 1.,
            delay_s,
            rise_s,
            fall_s,
            width_s: 0.1,
            period_s: 1.
        }
        .validate()
        .is_err());
    }
}
