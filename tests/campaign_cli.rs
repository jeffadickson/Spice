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
    assert_eq!(info["gpu"], cfg!(feature = "intel-opencl"));
    assert_eq!(info["nonlinear"]["diode_dc"], true);
    // Capabilities are a truthful contract for the bounded nonlinear configured path.
    assert_eq!(info["nonlinear"]["transient"], true);
    assert_eq!(info["nonlinear"]["configured_backward_euler"], true);
    assert_eq!(info["nonlinear"]["adaptive"], true);
    assert_eq!(info["nonlinear"]["source_overrides"], true);
    assert_eq!(info["nonlinear"]["gmin_dc"], true);
    assert_eq!(
        info["models"]["diode_with_capacitance"]["charge"],
        "constant_q_cv"
    );
    assert_eq!(info["models"]["diode_with_capacitance"]["depletion"], false);
    assert_eq!(
        info["models"]["diode_with_capacitance"]["transport_charge"],
        false
    );
    assert_eq!(
        info["models"]["diode_with_capacitance"]["configured_trapezoidal"],
        true
    );
    let output = Command::new(executable)
        .arg("--shell")
        .stdin(Stdio::null())
        .output()
        .unwrap();
    assert!(!output.status.success());
    let info: serde_json::Value = serde_json::from_slice(&output.stdout).unwrap();
    assert_eq!(info["complete"], false);
}

#[cfg(feature = "intel-opencl")]
#[test]
fn intel_metadata_process_releases_context_and_exits() {
    let mut child = Command::new(env!("CARGO_BIN_EXE_circuit-simulator"))
        .arg("--intel-device")
        .stdin(Stdio::null())
        .stdout(Stdio::piped())
        .stderr(Stdio::piped())
        .spawn()
        .unwrap();
    let started = std::time::Instant::now();
    while child.try_wait().unwrap().is_none() {
        if started.elapsed() > std::time::Duration::from_secs(30) {
            child.kill().unwrap();
            let _ = child.wait();
            panic!("Intel metadata emitted output but did not shut down within 30 seconds");
        }
        std::thread::sleep(std::time::Duration::from_millis(20));
    }
    let result = child.wait_with_output().unwrap();
    assert!(
        result.status.success(),
        "{}",
        String::from_utf8_lossy(&result.stderr)
    );
    let value: serde_json::Value = serde_json::from_slice(&result.stdout).unwrap();
    assert_eq!(value["backend"], "intel");
    assert_eq!(value["cpu_fallback"], false);
    assert!(value["device"].as_str().unwrap().contains("Intel"));
}

#[test]
fn campaign_case_count_bounds_are_inclusive_and_validate_before_publication() {
    let template: Request = serde_json::from_str(include_str!("../examples/divider.json")).unwrap();
    let registry = BackendRegistry::reference();
    for count in [0, 1, 63, 64, 65] {
        let requests: Vec<_> = (0..count)
            .map(|index| {
                let mut request = template.clone();
                request.case_id = format!("boundary-{count}-{index}");
                request
            })
            .collect();
        let expected: Vec<_> = requests.iter().map(|r| r.case_id.clone()).collect();
        let mut published = Vec::new();
        let result = run_cases(&requests, &registry, &false.into(), |outcome| {
            assert_eq!(outcome.execution, "succeeded");
            assert!(outcome.error.is_none());
            let manifest = outcome
                .result
                .as_ref()
                .expect("successful case retains its result");
            assert!(manifest.complete);
            assert_eq!(manifest.case_id, outcome.case_id);
            // Each case really solves the equal-resistor 10 V divider.
            let out = manifest
                .signals
                .iter()
                .position(|s| s.id == "v(out)")
                .unwrap();
            assert!((manifest.samples[0].values[out] - 5.).abs() < 1e-12);
            published.push(outcome.case_id.clone());
            Ok(())
        });
        if count == 0 || count == 65 {
            assert_eq!(
                result.unwrap_err().code,
                circuit_simulator::error::ErrorCode::InvalidInput
            );
            assert!(
                published.is_empty(),
                "invalid count published a partial campaign"
            );
        } else {
            result.unwrap();
            assert_eq!(
                published, expected,
                "valid campaign must preserve every case in order"
            );
        }
    }
}

#[test]
fn invalid_last_campaign_id_rejects_before_any_case_is_published() {
    let template: Request = serde_json::from_str(include_str!("../examples/divider.json")).unwrap();
    for duplicate in [false, true] {
        let mut requests: Vec<_> = (0..64)
            .map(|index| {
                let mut r = template.clone();
                r.case_id = format!("id-{index}");
                r
            })
            .collect();
        requests[63].case_id = if duplicate {
            requests[0].case_id.clone()
        } else {
            String::new()
        };
        let mut published = 0;
        let error = run_cases(
            &requests,
            &BackendRegistry::reference(),
            &false.into(),
            |_| {
                published += 1;
                Ok(())
            },
        )
        .unwrap_err();
        assert_eq!(
            error.code,
            circuit_simulator::error::ErrorCode::InvalidInput
        );
        assert_eq!(
            published, 0,
            "invalid late ID must not publish earlier cases"
        );
    }
}
