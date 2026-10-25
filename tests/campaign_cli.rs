use circuit_simulator::{api::Request, campaign::run_cases, registry::BackendRegistry};
use std::process::{Command, Stdio};
#[test]
fn mixed_campaign_retains_every_case_without_false_pass() {
    let a: Request = serde_json::from_str(include_str!("../examples/divider.json")).unwrap();
    let mut b = a.clone();
    b.case_id = "bad".into();
    b.backend = "missing".into();
    let mut outcomes = Vec::new();
    run_cases(&[a, b], &BackendRegistry::reference(), &false.into(), |c| {
        outcomes.push((c.case_id.clone(), c.execution));
        Ok(())
    })
    .unwrap();
    assert_eq!(outcomes[0].1, "succeeded");
    assert_eq!(outcomes[1].1, "failed");
}
#[test]
fn duplicate_campaign_ids_rejected() {
    let a: Request = serde_json::from_str(include_str!("../examples/divider.json")).unwrap();
    assert!(
        run_cases(
            &[a.clone(), a],
            &BackendRegistry::reference(),
            &false.into(),
            |_| Ok(())
        )
        .is_err()
    );
}
#[test]
fn cli_capabilities_and_invalid_arguments_are_json() {
    let executable = env!("CARGO_BIN_EXE_circuit-simulator");
    let output = Command::new(executable)
        .arg("--capabilities")
        .stdin(Stdio::null())
        .output()
        .unwrap();
    assert!(output.status.success());
    let info: serde_json::Value = serde_json::from_slice(&output.stdout).unwrap();
    assert_eq!(info["gpu"], false);
    assert_eq!(info["nonlinear"]["diode_dc"], true);
    assert_eq!(info["nonlinear"]["transient"], false);
    let output = Command::new(executable)
        .arg("--shell")
        .stdin(Stdio::null())
        .output()
        .unwrap();
    assert!(!output.status.success());
    let info: serde_json::Value = serde_json::from_slice(&output.stdout).unwrap();
    assert_eq!(info["complete"], false);
}
