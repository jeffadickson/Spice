use circuit_simulator::audit::{AuditOptions, Check, CheckStatus};
#[test]
fn audit_metrics_are_json_safe_and_bounds_are_validated() {
    for v in [f64::NAN, f64::INFINITY, -1., 0.] {
        assert!(
            AuditOptions {
                relative: v,
                ..AuditOptions::default()
            }
            .validate()
            .is_err()
        );
    }
    let c = Check::measured("overflow", f64::INFINITY, 1e-10, "A");
    assert_eq!(c.status, CheckStatus::Failed);
    assert_eq!(
        serde_json::to_value(c).unwrap()["discrepancy"],
        serde_json::Value::Null
    );
    assert_eq!(
        Check::measured("boundary", 1., 1., "V").status,
        CheckStatus::Passed
    );
}

#[test]
fn independent_kcl_detects_perturbed_current_and_rejects_malformed_states() {
    use circuit_simulator::{
        audit::dc, backend::DenseLu, compile::compile, dc::operating_point, netlist::parse,
    };
    let circuit = parse("title\nV1 a 0 10\nR1 a 0 1k").unwrap();
    let mut state = operating_point(&compile(&circuit).unwrap(), &DenseLu).unwrap();
    assert!(
        !dc(&circuit, &state, AuditOptions::default(), None)
            .unwrap()
            .has_failures()
    );
    state[1] += 0.001;
    assert!(
        dc(&circuit, &state, AuditOptions::default(), None)
            .unwrap()
            .has_failures()
    );
    for state in [vec![], vec![f64::NAN, 0.], vec![1.]] {
        assert!(dc(&circuit, &state, AuditOptions::default(), None).is_err());
    }
}
