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
fn measured_check_requires_each_finite_operand_and_nonnegative_tolerance() {
    for (discrepancy, tolerance) in [
        (0., f64::INFINITY), (0., f64::NEG_INFINITY), (0., f64::NAN),
        (f64::INFINITY, 1.), (f64::NEG_INFINITY, 1.), (f64::NAN, 1.),
        (0., -1.),
    ] {
        let check = Check::measured("invalid-input", discrepancy, tolerance, "A");
        assert_eq!(check.status, CheckStatus::Failed);
        assert!(check.reason.is_some());
        let json = serde_json::to_value(&check).unwrap();
        if !discrepancy.is_finite() { assert!(json["discrepancy"].is_null()); }
        if !tolerance.is_finite() || tolerance < 0. { assert!(json["tolerance"].is_null()); }
    }
    assert_eq!(Check::measured("exact-zero", 0., 0., "A").status, CheckStatus::Passed);
    assert_eq!(Check::measured("finite-failure", 2., 1., "A").status, CheckStatus::Failed);
}
#[test]
fn accumulation_mos_charge_audit_matches_independent_capacitor_and_common_mode() {
    use circuit_simulator::{audit::backward_euler_step, circuit::{Circuit, Element}, ids::NodeId as N, mos1::Mos1Model};
    // Deep accumulation has Cgb=Cox, Cgs=Cgd=0 and zero channel current.
    // Manufacture non-unit gate steps: independent I=Cox*deltaV/h fixes both
    // supplying source currents, without evaluating production charge code.
    let model = Mos1Model { p_channel:false, oxide_farads:3e-9,
        threshold_volts:0.7, beta_amps_per_volt_squared:2e-4,
        body_effect_sqrt_volts:0., surface_potential_volts:0.6,
        channel_length_modulation_per_volt:0. };
    for offset in [-1., 0., 2.] {
        for h in [1e-6, 2e-6, 5e-6] {
          for delta in [0.25, 1., 2.] {
            let volts = [1.+offset, -2.+offset, offset, -0.25+offset];
            let mut elements: Vec<Element> = ["VD", "VG", "VS", "VB"].into_iter().enumerate()
                .map(|(i,id)| Element::Voltage {id:id.into(),p:N(i+1),n:N(0),volts:volts[i]}).collect();
            elements.push(Element::Mos1Channel {id:"M".into(),p:N(1),n:N(3),gate:N(2),bulk:N(4),model});
            let circuit = Circuit {nodes:["0","d","g","s","b"].map(str::to_owned).to_vec(),elements};
            let current = 3e-9*delta/h;
            let mut state = volts.to_vec();
            state.extend([0., -current, 0., current]);
            let mut previous = state.clone(); previous[1] -= delta;
            let report = backward_euler_step(&circuit,&previous,&state,h,AuditOptions::default()).unwrap();
            let kcl:Vec<_> = report.checks.iter().filter(|c|c.id.starts_with("kcl:")).collect();
            assert_eq!(kcl.len(),5);
            assert!(kcl.iter().all(|c|c.status==CheckStatus::Passed),"offset={offset} h={h}: {kcl:?}");
            // The relative bound scales with the sum of absolute physical
            // currents, not their cancelling signed sum or a product.
            for check in &kcl {
                let scale = if ["kcl:0", "kcl:g", "kcl:b"].contains(&check.id.as_str()) {2.*current} else {0.};
                let options = AuditOptions::default();
                let expected = options.current_absolute_a + options.relative*scale;
                assert!((check.tolerance.unwrap()-expected).abs() <= expected*1e-12);
            }
            // A wrong terminal current must not be forgiven by the audit.
            state[5] += current*0.1;
            assert!(backward_euler_step(&circuit,&previous,&state,h,AuditOptions::default()).unwrap()
                .checks.iter().any(|c|c.id=="kcl:g" && c.status==CheckStatus::Failed));
          }
        }
    }
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

#[test]
fn topology_warns_about_capacitor_only_paths_and_identifies_source_conflicts() {
    use circuit_simulator::{netlist::parse, topology::inspect};
    let c = parse("title\nI1 0 a 1m\nC1 a 0 1u").unwrap();
    assert!(
        inspect(&c)
            .unwrap()
            .iter()
            .any(|d| d.code == "no_obvious_dc_reference_path" && d.subject == "a")
    );
    let c = parse("title\nV1 a 0 1\nV2 0 a -2").unwrap();
    assert!(
        inspect(&c)
            .unwrap()
            .iter()
            .any(|d| d.code == "conflicting_parallel_voltage_sources" && d.severity == "error")
    );
    let c = parse("title\nV1 a 0 1\nV2 0 a -1").unwrap();
    assert!(inspect(&c).unwrap().is_empty());
}

#[test]
fn backward_euler_energy_audit_includes_integration_dissipation() {
    use circuit_simulator::{audit::backward_euler_step, netlist::parse};
    let c = parse("title\nV1 a 0 1\nR1 a b 1k\nC1 b 0 1u").unwrap();
    let old = [0., 0., 0.];
    let next = [1., 0.5, -0.0005];
    let report = backward_euler_step(&c, &old, &next, 0.001, AuditOptions::default()).unwrap();
    assert!(!report.has_failures());
    assert!(
        backward_euler_step(
            &c,
            &old,
            &[1., 0.6, -0.0004],
            0.001,
            AuditOptions::default()
        )
        .unwrap()
        .has_failures()
    );
    for dt in [0., -1., f64::NAN, f64::INFINITY] {
        assert!(backward_euler_step(&c, &old, &next, dt, AuditOptions::default()).is_err());
    }
    let c = parse("title\nV1 a 0 1\nR1 a b 1\nL1 b 0 1m").unwrap();
    assert!(
        !backward_euler_step(
            &c,
            &old,
            &[1., 0.5, -0.5, 0.5],
            0.001,
            AuditOptions::default()
        )
        .is_ok()
    );
    let old = [0., 0., 0., 0.];
    assert!(
        !backward_euler_step(
            &c,
            &old,
            &[1., 0.5, -0.5, 0.5],
            0.001,
            AuditOptions::default()
        )
        .unwrap()
        .has_failures()
    );
}

#[test]
fn manifests_distinguish_physical_checks_and_unavailable_evidence() {
    use circuit_simulator::api::{Analysis, Request, run};
    let mut request: Request =
        serde_json::from_str(include_str!("../examples/divider.json")).unwrap();
    let manifest = run(&request).unwrap();
    assert!(!manifest.consistency.has_failures());
    assert!(
        manifest
            .consistency
            .checks
            .iter()
            .any(|c| c.id == "global_accuracy" && c.status == CheckStatus::NotChecked)
    );
    request.analysis = Analysis::Ac {
        frequencies_hz: vec![1.],
        source_phasors: vec![],
    };
    let ac = run(&request).unwrap();
    assert!(!ac.consistency.has_failures());
    assert!(
        ac.consistency
            .checks
            .iter()
            .any(|c| c.id == "ac_power_real" && c.status == CheckStatus::Passed)
    );
    assert!(
        ac.consistency
            .checks
            .iter()
            .any(|c| c.id == "global_accuracy" && c.status == CheckStatus::NotChecked)
    );
}

#[test]
fn debug_logging_keeps_protocol_stdout_clean() {
    use std::{
        io::Write,
        process::{Command, Stdio},
    };
    let mut child = Command::new(env!("CARGO_BIN_EXE_circuit-simulator"))
        .env("RUST_LOG", "circuit_simulator=debug")
        .stdin(Stdio::piped())
        .stdout(Stdio::piped())
        .stderr(Stdio::piped())
        .spawn()
        .unwrap();
    child
        .stdin
        .take()
        .unwrap()
        .write_all(include_bytes!("../examples/divider.json"))
        .unwrap();
    let output = child.wait_with_output().unwrap();
    assert!(output.status.success());
    let result: serde_json::Value = serde_json::from_slice(&output.stdout).unwrap();
    assert_eq!(result["status"], "succeeded");
    let logs = String::from_utf8(output.stderr).unwrap();
    assert!(!logs.is_empty());
    for line in logs.lines() {
        let _: serde_json::Value = serde_json::from_str(line).unwrap();
    }
    assert!(logs.contains("circuit_compiled") && logs.contains("physical_audit_completed"));
    assert!(!logs.contains("\"elements\""));
}

#[test]
fn timestep_refinement_compares_shared_samples_without_claiming_global_accuracy() {
    use circuit_simulator::{
        audit::compare_refinement,
        backend::DenseLu,
        compile::compile,
        netlist::parse,
        transient::{TransientOptions, transient},
    };
    let c = parse("title\nV1 a 0 1\nR1 a b 1k\nC1 b 0 1u").unwrap();
    let p = compile(&c).unwrap();
    let runs: Vec<_> = [0.001, 0.0005, 0.00025]
        .iter()
        .map(|h| {
            transient(
                &p,
                &DenseLu,
                &TransientOptions {
                    stop_s: 0.004,
                    step_s: *h,
                    max_steps: 32,
                },
            )
            .unwrap()
        })
        .collect();
    let a = compare_refinement(&runs[0], &runs[1], 1, 0.1, 1e-8, "V").unwrap();
    let b = compare_refinement(&runs[1], &runs[2], 1, 0.1, 1e-8, "V").unwrap();
    assert_eq!(a.status, CheckStatus::Passed);
    assert!(b.discrepancy.unwrap().abs() < a.discrepancy.unwrap().abs());
    assert_eq!(
        compare_refinement(&runs[0], &runs[1], 1, 1e-9, 1e-8, "V")
            .unwrap()
            .status,
        CheckStatus::Failed
    );
    assert!(compare_refinement(&runs[0], &runs[1], 99, 0.1, 1e-8, "V").is_err());
    assert!(compare_refinement(&runs[0], &runs[0], 1, 0.1, 1e-8, "V").is_err());
}

#[test]
fn loose_newton_convergence_cannot_bypass_physical_api_checks() {
    use circuit_simulator::{
        api::{Analysis, Request, run},
        error::ErrorCode,
        netlist::parse,
        newton::NewtonOptions,
    };
    let request = Request {
        schema_version: "1".into(),
        case_id: "loose".into(),
        backend: "lu".into(),
        experiment: None,
        circuit: parse("title\nV1 a 0 0.5\nR1 a b 1k\nD1 b 0 1p 0.02585").unwrap(),
        analysis: Analysis::OperatingPointNewton {
            options: NewtonOptions {
                voltage_absolute_tolerance: 1.,
                current_absolute_tolerance: 1.,
                relative_tolerance: 1.,
                ..NewtonOptions::default()
            },
        },
    };
    let error = run(&request).unwrap_err();
    assert_eq!(error.code, ErrorCode::NumericalFailure);
    assert!(error.message.contains("independent audit failed"));
}

#[test]
fn seeded_divider_audits_and_malformed_state_fuzz_smoke_are_reproducible() {
    use circuit_simulator::{
        audit::dc,
        circuit::{Circuit, Element},
        ids::NodeId as N,
    };
    // Fixed LCG seed; 128 independently solved resistor dividers and 512 raw-bit
    // state perturbations. Invalid/nonfinite states must fail safely, never panic.
    let mut seed = 0x4155_4449_545f_3331u64;
    let mut next = || {
        seed = seed.wrapping_mul(6364136223846793005).wrapping_add(1);
        seed
    };
    let mut last = None;
    for _ in 0..128 {
        let r1 = 1. + (next() % 100_000) as f64;
        let r2 = 1. + (next() % 100_000) as f64;
        let v = 0.1 + (next() % 10_000) as f64 / 100.;
        let c = Circuit {
            nodes: vec!["0".into(), "a".into(), "b".into()],
            elements: vec![
                Element::Voltage {
                    id: "V1".into(),
                    p: N(1),
                    n: N(0),
                    volts: v,
                },
                Element::Resistor {
                    id: "R1".into(),
                    p: N(1),
                    n: N(2),
                    ohms: r1,
                },
                Element::Resistor {
                    id: "R2".into(),
                    p: N(2),
                    n: N(0),
                    ohms: r2,
                },
            ],
        };
        let x = [v, v * r2 / (r1 + r2), -v / (r1 + r2)];
        assert!(
            !dc(&c, &x, AuditOptions::default(), None)
                .unwrap()
                .has_failures()
        );
        let mut wrong = x;
        wrong[2] += 0.01;
        assert!(
            dc(&c, &wrong, AuditOptions::default(), None)
                .unwrap()
                .has_failures()
        );
        last = Some(c);
    }
    let c = last.unwrap();
    for _ in 0..512 {
        let x = [
            f64::from_bits(next()),
            f64::from_bits(next()),
            f64::from_bits(next()),
        ];
        if let Ok(report) = dc(&c, &x, AuditOptions::default(), None) {
            serde_json::to_string(&report).unwrap();
        }
    }
}

#[test]
fn topology_errors_fail_before_solve_and_quiet_logs_leave_stdout_json() {
    use circuit_simulator::{
        api::{Request, run},
        circuit::Element,
        error::ErrorCode,
        ids::NodeId,
    };
    use std::{
        io::Write,
        process::{Command, Stdio},
    };
    let mut request: Request =
        serde_json::from_str(include_str!("../examples/divider.json")).unwrap();
    request.circuit.elements.push(Element::Voltage {
        id: "V2".into(),
        p: NodeId(1),
        n: NodeId(0),
        volts: 99.,
    });
    let error = run(&request).unwrap_err();
    assert_eq!(error.code, ErrorCode::InvalidInput);
    assert!(
        error
            .message
            .contains("conflicting_parallel_voltage_sources")
    );
    let mut child = Command::new(env!("CARGO_BIN_EXE_circuit-simulator"))
        .env("RUST_LOG", "off")
        .stdin(Stdio::piped())
        .stdout(Stdio::piped())
        .stderr(Stdio::piped())
        .spawn()
        .unwrap();
    child
        .stdin
        .take()
        .unwrap()
        .write_all(include_bytes!("../examples/divider.json"))
        .unwrap();
    let output = child.wait_with_output().unwrap();
    assert!(output.status.success() && output.stderr.is_empty());
    let _: serde_json::Value = serde_json::from_slice(&output.stdout).unwrap();
}
