use circuit_simulator::{
    api::{Analysis, Request, run},
    artifact,
    configured::TransientConfig,
    experiment::{ExperimentSpec, MeasurementSpec, Operation},
    expression::Expr,
    netlist::parse,
    sources::SourceOverride,
    waveform::{Point, Waveform},
};
fn result() -> circuit_simulator::api::Manifest {
    let r: Request = serde_json::from_str(include_str!("../examples/divider.json")).unwrap();
    run(&r).unwrap()
}
fn root() -> std::path::PathBuf {
    static COUNTER: std::sync::atomic::AtomicU64 = std::sync::atomic::AtomicU64::new(0);
    let stamp = std::time::SystemTime::now()
        .duration_since(std::time::UNIX_EPOCH)
        .unwrap()
        .as_nanos();
    // Test artifacts follow the caller's TMP/TEMP (E: on the Windows host),
    // rather than creating build/test output beneath the source checkout.
    std::env::temp_dir().join("spice-artifact-tests").join(format!(
        "{}-{stamp}-{}",
        std::process::id(),
        COUNTER.fetch_add(1, std::sync::atomic::Ordering::Relaxed)
    ))
}
#[test]
fn artifact_roundtrip_collision_and_corruption() {
    let root = root();
    let result = result();
    let directory = artifact::publish(&root, "run-1", &result).unwrap();
    assert_eq!(
        artifact::read_verified(&directory).unwrap()["case_id"],
        "divider"
    );
    assert!(artifact::publish(&root, "run-1", &result).is_err());
    std::fs::write(directory.join("result.json"), b"{}").unwrap();
    assert!(artifact::read_verified(&directory).is_err());
}
#[test]
fn artifact_paths_and_incomplete_results_are_rejected() {
    let root = root();
    let mut result = result();
    assert!(artifact::publish(&root, "../escape", &result).is_err());
    result.complete = false;
    assert!(artifact::publish(&root, "partial", &result).is_err());
    assert!(!root.exists());
}
#[test]
fn payload_length_and_digest_are_independent_integrity_requirements() {
    let root = root();
    let payload = result();
    let changed_hash = artifact::publish(&root, "same-length-tamper", &payload).unwrap();
    let path = changed_hash.join("result.json");
    let original = std::fs::read_to_string(&path).unwrap();
    let tampered = original.replacen("divider", "dividex", 1);
    assert_ne!(original, tampered);
    assert_eq!(original.len(), tampered.len());
    // Still-valid JSON and identical length isolate the digest check.
    serde_json::from_str::<serde_json::Value>(&tampered).unwrap();
    std::fs::write(&path, tampered).unwrap();
    let error = artifact::read_verified(&changed_hash).unwrap_err();
    assert_eq!(error.code, circuit_simulator::error::ErrorCode::InvalidInput);

    let wrong_length = artifact::publish(&root, "length-marker-only", &payload).unwrap();
    let marker_path = wrong_length.join("manifest.json");
    let mut marker: serde_json::Value = serde_json::from_slice(&std::fs::read(&marker_path).unwrap()).unwrap();
    marker["bytes"] = (marker["bytes"].as_u64().unwrap() + 1).into();
    // The payload and its SHA256 are unchanged: only the declared length fails.
    std::fs::write(marker_path, serde_json::to_vec(&marker).unwrap()).unwrap();
    let error = artifact::read_verified(&wrong_length).unwrap_err();
    assert_eq!(error.code, circuit_simulator::error::ErrorCode::InvalidInput);
}
#[test]
fn publication_metadata_fields_are_independently_required_before_storage() {
    // A valid complete flag must not excuse failed status or unknown schema.
    // Exercise each independent admission field and their combinations.
    for complete in [false, true] {
        for status in ["succeeded", "failed"] {
            for schema in ["1", "future"] {
                if complete && status == "succeeded" && schema == "1" { continue; }
                let root = root();
                let mut result = result();
                result.complete = complete;
                result.status = status.into();
                result.schema_version = schema.into();
                let error = artifact::publish(&root, "metadata", &result).unwrap_err();
                assert_eq!(error.code, circuit_simulator::error::ErrorCode::InvalidInput);
                assert!(!root.exists(), "rejected metadata must not publish partial storage");
            }
        }
    }
}
#[test]
fn independent_dc_power_accounting_balances_source_and_load() {
    let result = result();
    let p = result.dc_power.unwrap();
    assert!(p.sum_absorbed_w.abs() < 1e-12);
    let source = p.devices.iter().find(|p| p.id == "V1").unwrap();
    assert!((source.absorbed_w + 0.05).abs() < 1e-12);
    assert!((p.sum_absolute_w - 0.1).abs() < 1e-12);
}
#[test]
fn scalar_domains_and_derivatives_match_finite_differences() {
    let e = Expr::Divide(
        Box::new(Expr::Exp(Box::new(Expr::Input))),
        Box::new(Expr::Add(
            Box::new(Expr::Input),
            Box::new(Expr::Constant(2.)),
        )),
    );
    for x in [-1., 0.3, 2.] {
        let (_, d) = e.evaluate(x).unwrap();
        let h = 1e-6;
        let numerical = (e.evaluate(x + h).unwrap().0 - e.evaluate(x - h).unwrap().0) / (2. * h);
        assert!((d - numerical).abs() < 1e-6);
    }
    assert!(
        Expr::Log(Box::new(Expr::Constant(-1.)))
            .evaluate(0.)
            .is_err()
    );
    assert!(
        Expr::Divide(Box::new(Expr::Input), Box::new(Expr::Constant(0.)))
            .evaluate(1.)
            .is_err()
    );
    assert!(
        Expr::Exp(Box::new(Expr::Constant(1000.)))
            .evaluate(0.)
            .is_err()
    );
}
#[test]
fn recorded_linear_ramp_rms_and_integral_are_analytic() {
    let circuit = parse("title\nV1 a 0 0\nR1 a 0 1k").unwrap();
    let options = TransientConfig {
        stop_s: 2.,
        max_step_s: 0.5,
        sources: vec![SourceOverride {
            id: "V1".into(),
            waveform: Waveform::Pwl {
                points: vec![
                    Point {
                        time_s: 0.,
                        value: 0.,
                    },
                    Point {
                        time_s: 2.,
                        value: 2.,
                    },
                ],
            },
        }],
        ..Default::default()
    };
    let operation = |id: &str, operation| MeasurementSpec {
        id: id.into(),
        signal: "v(a)".into(),
        operation,
    };
    let request = Request {
        schema_version: "1".into(),
        case_id: "ramp".into(),
        circuit,
        analysis: Analysis::TransientConfigured { options },
        backend: "faer".into(),
        experiment: Some(ExperimentSpec {
            measurements: vec![
                operation(
                    "rms",
                    Operation::Rms {
                        start_s: 0.,
                        stop_s: 2.,
                    },
                ),
                operation(
                    "integral",
                    Operation::Integral {
                        start_s: 0.,
                        stop_s: 2.,
                    },
                ),
            ],
            assertions: Vec::new(),
        }),
    };
    let r = run(&request).unwrap();
    assert!((r.measurements[0].value.unwrap() - 2. / 3_f64.sqrt()).abs() < 1e-12);
    assert!((r.measurements[1].value.unwrap() - 2.).abs() < 1e-12);
}
