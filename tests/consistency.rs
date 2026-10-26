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

#[test]
fn voltage_constraint_catches_a_kcl_consistent_wrong_solution() {
    use circuit_simulator::{audit::dc, netlist::parse};
    let circuit = parse("title\nV1 a 0 10\nR1 a 0 1k").unwrap();
    // Current exactly balances the resistor, but the ideal source is wrong.
    let report = dc(&circuit, &[9., -0.009], AuditOptions::default(), None).unwrap();
    assert!(
        report
            .checks
            .iter()
            .filter(|c| c.id.starts_with("kcl:"))
            .all(|c| c.status == CheckStatus::Passed)
    );
    assert_eq!(
        report
            .checks
            .iter()
            .find(|c| c.id == "constraint:V1")
            .unwrap()
            .status,
        CheckStatus::Failed
    );
}

#[test]
fn controlled_voltage_and_inductor_constraints_are_audited() {
    use circuit_simulator::{
        audit::dc, backend::DenseLu, compile::compile, dc::operating_point, netlist::parse,
    };
    for deck in [
        "title\nV1 a 0 2\nE1 b 0 a 0 3\nR1 b 0 1k",
        "title\nV1 a 0 2\nR1 a b 1k\nL1 b 0 1m",
        "title\nV1 a 0 2\nR1 a 0 1k\nH1 b 0 V1 100\nR2 b 0 1k",
    ] {
        let c = parse(deck).unwrap();
        let x = operating_point(&compile(&c).unwrap(), &DenseLu).unwrap();
        assert!(
            !dc(&c, &x, AuditOptions::default(), None)
                .unwrap()
                .has_failures()
        );
    }
}

#[test]
fn power_audit_includes_shunt_dissipation_and_detects_wrong_source_current() {
    use circuit_simulator::{
        audit::dc,
        backend::DenseLu,
        compile::compile,
        netlist::parse,
        newton::{NewtonOptions, solve},
    };
    let c = parse("title\nV1 a 0 1\nR1 a b 1k\nD1 b 0 1p 0.02585").unwrap();
    let (mut x, _) = solve(
        &compile(&c).unwrap(),
        &DenseLu,
        NewtonOptions {
            gmin_siemens: Some(1e-3),
            ..NewtonOptions::default()
        },
    )
    .unwrap();
    let report = dc(&c, &x, AuditOptions::default(), Some(1e-3)).unwrap();
    assert!(!report.has_failures());
    assert!(
        report
            .checks
            .iter()
            .any(|c| c.id == "unregularized_physics" && c.status == CheckStatus::NotChecked)
    );
    assert!(
        dc(&c, &x, AuditOptions::default(), None)
            .unwrap()
            .has_failures()
    );
    *x.last_mut().unwrap() += 0.001;
    assert_eq!(
        dc(&c, &x, AuditOptions::default(), Some(1e-3))
            .unwrap()
            .checks
            .iter()
            .find(|c| c.id == "power_balance")
            .unwrap()
            .status,
        CheckStatus::Failed
    );
}
