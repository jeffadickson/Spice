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
