//! Independent acceptance, audit and resource-boundary cases from shard 12/16.
use circuit_simulator::{
    api::{self, Analysis, Request},
    audit::{self, AuditOptions, AuditReport, Check, CheckStatus},
    backend::DenseLu,
    compile::compile,
    configured::{self, TransientConfig},
    error::ErrorCode,
    experiment::{AssertionSpec, ExperimentSpec, MeasurementSpec, Operation},
    linalg::{Matrix, PreparedReal},
    netlist::parse,
    waveform::Waveform,
};

#[test]
fn independent_policy_fields_reject_invalid_values_and_accept_exact_limits() {
    let valid = circuit_simulator::adaptive::AdaptiveOptions { current_relative: None,
        relative: 0.01,
        voltage_absolute: 1e-6,
        current_absolute: 1e-12,
        min_step_s: 1e-9,
        max_attempts: 1_000_000,
    };
    assert!(valid.validate(1e-3).is_ok());
    for attempts in [0, 1_000_001] {
        let mut bad = valid.clone();
        bad.max_attempts = attempts;
        assert!(bad.validate(1e-3).is_err());
    }
    let bad = circuit_simulator::newton::NewtonOptions {
        current_absolute_tolerance: f64::NAN,
        ..Default::default()
    };
    assert!(bad.validate().is_err());
    let mut circuit = parse("title\nV1 out 0 2\nR1 out 0 2").unwrap();
    circuit.nodes[1] = "n".repeat(256);
    assert!(circuit.validate().is_ok());
    circuit.nodes[1].push('n');
    assert!(circuit.validate().is_err());
    let matrix = Matrix::zeros(256).unwrap();
    assert!(matrix.validate(&vec![0.; 256]).is_ok());
    let grid = circuit_simulator::variation::ScalarGrid {
        dimensions: vec![
            circuit_simulator::variation::ScalarSweep {
                element_id: "R1".into(),
                values: vec![1.; 8],
            },
            circuit_simulator::variation::ScalarSweep {
                element_id: "R2".into(),
                values: vec![2.; 8],
            },
        ],
    };
    assert_eq!(grid.cardinality().unwrap(), 64);
    let mut oversized = grid;
    oversized.dimensions[1].values.push(2.);
    assert!(oversized.validate().is_err());
}

#[test]
fn derived_dc_diode_current_uses_signed_difference_of_two_live_nodes() {
    let request = Request {
        schema_version: "1".into(),
        case_id: "floating-diode-dc".into(),
        circuit: parse("title\nV1 p 0 0.1\nV2 n 0 0.2\nD1 p n 1u 25m").unwrap(),
        analysis: Analysis::OperatingPoint,
        backend: "lu".into(),
        experiment: None,
    };
    let result = api::run(&request).unwrap();
    let index = result.signals.iter().position(|s| s.id == "i(D1)").unwrap();
    // Clamped p/n voltages make the signed Shockley reference independent of MNA.
    let expected = 1e-6 * (-4_f64).exp_m1();
    assert!((result.samples[0].values[index] - expected).abs() < 1e-15);
}

#[test]
fn point_assertions_accept_inclusive_equal_bounds() {
    use circuit_simulator::assertion::{Outcome, within};
    assert_eq!(within(Some(2.5), 2.5, 2.5), Outcome::Pass);
    assert_eq!(within(Some(2.6), 2.5, 2.5), Outcome::Fail);
    assert_eq!(within(None, 2.5, 2.5), Outcome::Indeterminate);
}

#[test]
fn fixed_gmin_audit_adds_shunt_magnitude_to_the_current_scale() {
    let circuit = parse("title\nI1 0 out 1.5\nR1 out 0 2").unwrap();
    let options = AuditOptions {
        relative: 0.1,
        ..Default::default()
    };
    let report = audit::dc(&circuit, &[2.], options, Some(0.25)).unwrap();
    let check = report.checks.iter().find(|c| c.id == "kcl:out").unwrap();
    // Resistor 1 A, source 1.5 A, shunt 0.5 A: scale is 3 A, residual is zero.
    assert_eq!(check.status, CheckStatus::Passed);
    assert!((check.tolerance.unwrap() - (options.current_absolute_a + 0.1 * 3.)).abs() < 1e-14);
}

#[test]
fn inductor_audit_uses_sum_of_voltage_magnitudes_and_retains_worst_evidence() {
    let circuit = parse("title\nR1 out 0 3\nL1 out 0 3").unwrap();
    let options = AuditOptions::default();
    // Source-free BE decay: i_L 2 -> 1 A, L=3 H, h=1 s, v_L=-3 V.
    let report = audit::backward_euler_step(&circuit, &[0., 2.], &[-3., 1.], 1., options).unwrap();
    let check = report
        .checks
        .iter()
        .find(|c| c.id == "constraint:L1")
        .unwrap();
    assert_eq!(check.status, CheckStatus::Passed);
    assert!(
        (check.tolerance.unwrap() - (options.voltage_absolute_v + 6. * options.relative)).abs()
            < 1e-20
    );
    let mut merged = AuditReport {
        checks: vec![Check::measured("same", 0.2, 1., "A")],
    };
    merged.merge_worst(AuditReport {
        checks: vec![Check::measured("same", 1.5, 1., "A")],
    });
    assert_eq!(merged.checks[0].discrepancy, Some(1.5));
    assert_eq!(merged.checks[0].status, CheckStatus::Failed);
}

#[test]
fn algebraic_initial_state_acceptance_uses_rhs_scaled_absolute_bound() {
    let plan = compile(&parse("title\nV1 out 0 2\nR1 out 0 1").unwrap()).unwrap();
    // KCL is exact; the 1e-10 V branch discrepancy lies below 3e-10 V,
    // but above the erroneous reciprocal-RHS bound 1e-10/3 V.
    let near = 2. + 1e-10;
    let options = TransientConfig {
        stop_s: 1e-6,
        max_step_s: 1e-6,
        max_steps: 1,
        initial_state: Some(vec![near, -near]),
        ..Default::default()
    };
    assert!(configured::run(&plan, &DenseLu, &options, &false.into()).is_ok());
}

#[test]
fn measurement_identity_and_assertion_unit_bounds_are_independent() {
    let measurement = MeasurementSpec {
        id: "m".into(),
        signal: "v(out)".into(),
        operation: Operation::ValueAt { time_s: 0. },
    };
    for (id, signal) in [
        (String::new(), "v(out)".into()),
        ("x".repeat(257), "v(out)".into()),
        ("m".into(), "x".repeat(513)),
    ] {
        let spec = ExperimentSpec {
            measurements: vec![MeasurementSpec {
                id,
                signal,
                ..measurement.clone()
            }],
            assertions: vec![],
        };
        assert!(spec.validate().is_err());
    }
    let mut spec = ExperimentSpec {
        measurements: vec![measurement],
        assertions: vec![AssertionSpec {
            id: "a".into(),
            measurement: "m".into(),
            unit: "x".repeat(64),
            low: 0.,
            high: 1.,
        }],
    };
    assert!(spec.validate().is_ok());
    spec.assertions[0].unit.push('x');
    assert!(spec.validate().is_err());
}

#[test]
fn prepared_dense_callback_rejects_wrong_length_or_nonfinite_output_separately() {
    for values in [vec![], vec![f64::NAN]] {
        let matrix = Matrix {
            n: 1,
            values: vec![2.],
        };
        let prepared = PreparedReal::new(matrix, move |_| Ok(values.clone())).unwrap();
        assert_eq!(
            prepared.solve(&[4.]).unwrap_err().code,
            ErrorCode::NumericalFailure
        );
    }
}

#[test]
fn ngspice_deck_exact_byte_limit_remains_accepted() {
    let mut deck = "title\nV1 out 0 1\nR1 out 0 1k\n*".to_owned();
    deck.extend(std::iter::repeat_n(' ', 1_048_576 - deck.len()));
    assert!(circuit_simulator::netlist::parse_ngspice_basic(&deck).is_ok());
    deck.push(' ');
    assert_eq!(
        circuit_simulator::netlist::parse_ngspice_basic(&deck)
            .unwrap_err()
            .code,
        ErrorCode::ResourceExhausted
    );
}

#[test]
fn pulse_and_exponential_fields_have_separate_finite_and_sign_domains() {
    let pulse = Waveform::Pulse {
        low: 0.,
        high: 2.,
        delay_s: 0.,
        rise_s: 1.,
        fall_s: 1.,
        width_s: 0.,
        period_s: 4.,
    };
    assert!(pulse.validate().is_ok());
    for (low, high, width_s) in [(f64::NAN, 2., 0.), (0., f64::NAN, 0.), (0., 2., -0.5)] {
        let invalid = Waveform::Pulse {
            low,
            high,
            delay_s: 0.,
            rise_s: 1.,
            fall_s: 1.,
            width_s,
            period_s: 4.,
        };
        assert!(invalid.validate().is_err());
    }
    let exponential = Waveform::Exponential {
        initial: 0.,
        pulsed: 2.,
        rise_delay_s: 0.,
        rise_tau_s: 1.,
        fall_delay_s: 2.,
        fall_tau_s: 1.,
    };
    assert!(exponential.validate().is_ok());
    for tau in [0., f64::NAN] {
        let invalid = Waveform::Exponential {
            initial: 0.,
            pulsed: 2.,
            rise_delay_s: 0.,
            rise_tau_s: tau,
            fall_delay_s: 2.,
            fall_tau_s: 1.,
        };
        assert!(invalid.validate().is_err());
    }
}

#[test]
fn artifact_payload_at_limit_cannot_hide_an_extra_trailing_byte() {
    use sha2::{Digest, Sha256};
    use std::io::Write;
    let parent = std::env::temp_dir();
    assert!(parent.is_dir());
    let root = parent.join(format!("spice-oct28-artifact-{}", std::process::id()));
    assert!(!root.exists());
    std::fs::create_dir(&root).unwrap();
    // Exercise the real 64 MiB policy, including a valid maximum-sized payload.
    let limit = 64 * 1024 * 1024;
    let mut bytes = br#"{"schema_version":"1","status":"succeeded","complete":true}"#.to_vec();
    bytes.resize(limit, b' ');
    let hash = format!("{:x}", Sha256::digest(&bytes));
    std::fs::write(root.join("result.json"), &bytes).unwrap();
    drop(bytes);
    let marker = serde_json::json!({"schema_version":"1","complete":true,"path":"result.json","bytes":limit,"sha256":hash});
    std::fs::write(
        root.join("manifest.json"),
        serde_json::to_vec(&marker).unwrap(),
    )
    .unwrap();
    let boundary_ok = circuit_simulator::artifact::read_verified(&root).is_ok();
    std::fs::OpenOptions::new()
        .append(true)
        .open(root.join("result.json"))
        .unwrap()
        .write_all(b" ")
        .unwrap();
    let oversize_rejected = circuit_simulator::artifact::read_verified(&root).is_err();
    assert_eq!(root.parent(), Some(parent.as_path()));
    std::fs::remove_dir_all(root).unwrap();
    assert!(boundary_ok && oversize_rejected);
}
