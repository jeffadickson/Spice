//! Audits must reject physically corrupted states even when shapes remain valid.
use circuit_simulator::{
    ac::ac_with_source_phasors,
    api::AcSourcePhasor,
    audit::{self, AuditOptions, CheckStatus},
    backend::DenseLu,
    backend_faer::FaerSparseLu,
    compile::compile,
    configured::{Method, TransientConfig, run},
    netlist::parse,
    sources::SourceOverride,
    waveform::Waveform,
};

fn rc() -> circuit_simulator::circuit::Circuit {
    parse("RC\nV1 in 0 1\nR1 in out 1000\nC1 out 0 1u\n.end").unwrap()
}

#[test]
fn linear_ac_checks_complex_drive_kcl_and_peak_power() {
    let circuit = rc();
    let plan = compile(&circuit).unwrap();
    let drive = [AcSourcePhasor {
        id: "V1".into(),
        real: 1.,
        imag: 2.,
    }];
    let mut samples =
        ac_with_source_phasors(&plan, &[1., 100., 100000.], &drive, &FaerSparseLu).unwrap();
    for sample in &samples {
        let r = audit::ac(&circuit, sample, &drive, AuditOptions::default()).unwrap();
        assert!(!r.has_failures(), "{r:?}");
        assert!(r.checks.iter().all(|c| c.status == CheckStatus::Passed));
    }
    let sample = &mut samples[0];
    sample.values[plan.branch_index("V1").unwrap()].real += 0.001;
    sample.values[plan.branch_index("V1").unwrap()].imag += 0.002;
    let report = audit::ac(&circuit, sample, &drive, AuditOptions::default()).unwrap();
    assert!(report.has_failures());
    let power = report
        .checks
        .iter()
        .find(|c| c.id == "ac_power_real")
        .unwrap();
    assert!(
        (power.discrepancy.unwrap() - 0.0025).abs() < 1e-14,
        "Peak rather than RMS power"
    );
    sample.values[0].imag = -2.;
    assert!(
        audit::ac(&circuit, sample, &drive, AuditOptions::default())
            .unwrap()
            .checks
            .iter()
            .any(|c| c.id == "constraint:V1" && c.status == CheckStatus::Failed)
    );
}

#[test]
fn configured_rlc_checks_signed_flux_and_midpoint_stored_energy() {
    let circuit =
        parse("RLC\nV1 in 0 1\nR1 in mid 100\nL1 mid out 10m\nC1 out 0 1u\n.end").unwrap();
    let plan = compile(&circuit).unwrap();
    for method in [Method::BackwardEuler, Method::Trapezoidal] {
        let config = TransientConfig {
            stop_s: 0.001,
            max_step_s: 1e-5,
            max_steps: 200,
            method,
            initial_state: Some(vec![1., 1., 0., 0., 0.]),
            sources: vec![],
            adaptive: None,
        };
        let result = run(&plan, &FaerSparseLu, &config, &false.into()).unwrap();
        for pair in result.samples.windows(2) {
            let report = audit::configured_step(
                &circuit,
                &pair[0],
                &pair[1],
                &config,
                AuditOptions::default(),
            )
            .unwrap();
            assert!(!report.has_failures(), "{method:?}: {report:?}");
            let mut corrupt = pair[1].clone();
            corrupt.values[plan.branch_index("L1").unwrap()] += 0.01;
            assert!(
                audit::configured_step(
                    &circuit,
                    &pair[0],
                    &corrupt,
                    &config,
                    AuditOptions::default()
                )
                .unwrap()
                .has_failures()
            );
        }
    }
}

#[test]
fn ac_rlc_and_controlled_source_profiles_are_independently_audited() {
    for deck in [
        "RLC\nV1 in 0 1\nR1 in mid 100\nL1 mid out 10m\nC1 out 0 1u\n.end",
        "controls\nV1 in 0 1\nR1 in 0 1000\nE1 e 0 in 0 2\nR2 e 0 2000\nG1 g 0 in 0 0.001\nR3 g 0 1000\nF1 f 0 V1 2\nR4 f 0 1000\nH1 h 0 V1 1000\nR5 h 0 1000\n.end",
    ] {
        let circuit = parse(deck).unwrap();
        let plan = compile(&circuit).unwrap();
        for sample in
            ac_with_source_phasors(&plan, &[1., 1000., 100000.], &[], &FaerSparseLu).unwrap()
        {
            let r = audit::ac(&circuit, &sample, &[], AuditOptions::default()).unwrap();
            assert!(!r.has_failures(), "{r:?}");
        }
    }
}

#[test]
fn malformed_ac_audit_inputs_are_rejected_and_nonlinear_bias_is_unavailable() {
    let circuit = rc();
    let plan = compile(&circuit).unwrap();
    let mut sample = ac_with_source_phasors(&plan, &[1.], &[], &DenseLu)
        .unwrap()
        .remove(0);
    let bad = [AcSourcePhasor {
        id: "R1".into(),
        real: 1.,
        imag: 0.,
    }];
    assert!(audit::ac(&circuit, &sample, &bad, AuditOptions::default()).is_err());
    sample.frequency_hz = 0.;
    assert!(audit::ac(&circuit, &sample, &[], AuditOptions::default()).is_err());
    sample.frequency_hz = 1.;
    sample.values.pop();
    assert!(audit::ac(&circuit, &sample, &[], AuditOptions::default()).is_err());
    let diode = parse("diode\nV1 out 0 0.3\nD1 out 0 1p 25m\n.end").unwrap();
    let nonlinear = ac_with_source_phasors(&compile(&diode).unwrap(), &[1.], &[], &DenseLu)
        .unwrap()
        .remove(0);
    let r = audit::ac(&diode, &nonlinear, &[], AuditOptions::default()).unwrap();
    assert!(r.checks.iter().any(|c| c.status == CheckStatus::NotChecked));
    assert!(!r.checks.iter().any(|c| c.status == CheckStatus::Passed));
}

fn config(method: Method) -> TransientConfig {
    TransientConfig {
        method,
        stop_s: 0.004,
        max_step_s: 0.0001,
        max_steps: 100,
        initial_state: Some(vec![1., 0., -0.001]),
        sources: vec![SourceOverride {
            id: "V1".into(),
            waveform: Waveform::Sine {
                offset: 1.,
                amplitude: 0.5,
                frequency_hz: 100.,
                phase_rad: 0.,
            },
        }],
        adaptive: None,
    }
}

#[test]
fn configured_fixed_methods_check_endpoint_sources_and_discrete_energy() {
    let circuit = rc();
    let plan = compile(&circuit).unwrap();
    for method in [Method::BackwardEuler, Method::Trapezoidal] {
        let config = config(method);
        let result = run(&plan, &FaerSparseLu, &config, &false.into()).unwrap();
        for pair in result.samples.windows(2) {
            let report = audit::configured_step(
                &circuit,
                &pair[0],
                &pair[1],
                &config,
                AuditOptions::default(),
            )
            .unwrap();
            assert!(!report.has_failures(), "{method:?} {report:?}");
            let mut corrupt = pair[1].clone();
            corrupt.values[1] += 0.01;
            let report = audit::configured_step(
                &circuit,
                &pair[0],
                &corrupt,
                &config,
                AuditOptions::default(),
            )
            .unwrap();
            assert!(report.has_failures());
            assert!(
                report
                    .checks
                    .iter()
                    .any(|c| c.unit == "J" && c.status == CheckStatus::Failed)
            );
        }
    }
}

#[test]
fn configured_invalid_interval_and_adaptive_hidden_history_are_explicit() {
    let circuit = rc();
    let plan = compile(&circuit).unwrap();
    let mut config = config(Method::BackwardEuler);
    let result = run(&plan, &DenseLu, &config, &false.into()).unwrap();
    let pair = &result.samples[..2];
    assert!(
        audit::configured_step(
            &circuit,
            &pair[1],
            &pair[0],
            &config,
            AuditOptions::default()
        )
        .is_err()
    );
    config.adaptive = Some(circuit_simulator::adaptive::AdaptiveOptions {
        relative: 1e-3,
        voltage_absolute: 1e-6,
        current_absolute: 1e-9,
        min_step_s: 1e-8,
        max_attempts: 1000,
    });
    let report = audit::configured_step(
        &circuit,
        &pair[0],
        &pair[1],
        &config,
        AuditOptions::default(),
    )
    .unwrap();
    assert!(
        report
            .checks
            .iter()
            .all(|c| c.status == CheckStatus::NotChecked)
    );
}

#[test]
fn public_manifests_publish_automatic_audit_evidence() {
    for (json, check) in [
        (
            include_str!("../compat/benchmarks/rc-ac.json"),
            "ac_power_imag",
        ),
        (
            include_str!("../compat/benchmarks/rc-transient.json"),
            "trap_energy_balance",
        ),
    ] {
        let request: circuit_simulator::api::Request = serde_json::from_str(json).unwrap();
        let manifest = circuit_simulator::api::run(&request).unwrap();
        assert!(
            manifest
                .consistency
                .checks
                .iter()
                .any(|c| c.id == check && c.status == CheckStatus::Passed)
        );
        assert!(
            manifest
                .consistency
                .checks
                .iter()
                .any(|c| c.id == "global_accuracy" && c.status == CheckStatus::NotChecked)
        );
    }
}

#[test]
fn ac_selected_source_replaces_extreme_nominal_without_cancellation() {
    let circuit = parse("title\nV1 out 0 1e308\nR1 out 0 1000\n.end").unwrap();
    let source = [AcSourcePhasor {
        id: "V1".into(),
        real: 0.25,
        imag: -0.5,
    }];
    let sample =
        ac_with_source_phasors(&compile(&circuit).unwrap(), &[10.], &source, &FaerSparseLu)
            .unwrap()
            .remove(0);
    assert!(
        !audit::ac(&circuit, &sample, &source, AuditOptions::default())
            .unwrap()
            .has_failures()
    );
    let duplicate = [source[0].clone(), source[0].clone()];
    assert!(audit::ac(&circuit, &sample, &duplicate, AuditOptions::default()).is_err());
    let mut bad = sample;
    bad.values[0].real = f64::NAN;
    assert!(audit::ac(&circuit, &bad, &source, AuditOptions::default()).is_err());
}

#[test]
fn configured_nonlinear_be_constant_charge_matches_independent_energy() {
    use circuit_simulator::{
        circuit::{Circuit, Element},
        ids::NodeId,
        waveform::Point,
    };
    let circuit = Circuit {
        nodes: vec!["0".into(), "out".into()],
        elements: vec![
            Element::Voltage {
                id: "V1".into(),
                p: NodeId(1),
                n: NodeId(0),
                volts: 0.1,
            },
            Element::DiodeWithCapacitance {
                id: "D1".into(),
                p: NodeId(1),
                n: NodeId(0),
                saturation_amps: 1e-12,
                thermal_volts: 0.025,
                farads: 1e-6,
            },
        ],
    };
    let config = TransientConfig {
        stop_s: 0.001,
        max_step_s: 0.0001,
        max_steps: 100,
        method: Method::BackwardEuler,
        initial_state: Some(vec![0., 0.]),
        adaptive: None,
        sources: vec![SourceOverride {
            id: "V1".into(),
            waveform: Waveform::Pwl {
                points: vec![
                    Point {
                        time_s: 0.,
                        value: 0.,
                    },
                    Point {
                        time_s: 0.001,
                        value: 0.1,
                    },
                ],
            },
        }],
    };
    let result = run(
        &compile(&circuit).unwrap(),
        &FaerSparseLu,
        &config,
        &false.into(),
    )
    .unwrap();
    for pair in result.samples.windows(2) {
        assert!(
            !audit::configured_step(
                &circuit,
                &pair[0],
                &pair[1],
                &config,
                AuditOptions::default()
            )
            .unwrap()
            .has_failures()
        );
    }
    let mut trap = config.clone();
    trap.method = Method::Trapezoidal;
    let report = audit::configured_step(
        &circuit,
        &result.samples[0],
        &result.samples[1],
        &trap,
        AuditOptions::default(),
    )
    .unwrap();
    assert!(
        report
            .checks
            .iter()
            .all(|c| c.status == CheckStatus::NotChecked)
    );
}

#[test]
fn configured_rejects_duplicate_unknown_and_non_source_overrides() {
    let circuit = rc();
    let config = config(Method::BackwardEuler);
    let result = run(
        &compile(&circuit).unwrap(),
        &DenseLu,
        &config,
        &false.into(),
    )
    .unwrap();
    for id in ["V1", "missing", "R1"] {
        let mut bad = config.clone();
        let mut source = bad.sources[0].clone();
        source.id = id.into();
        bad.sources.push(source);
        assert!(
            audit::configured_step(
                &circuit,
                &result.samples[0],
                &result.samples[1],
                &bad,
                AuditOptions::default()
            )
            .is_err()
        );
    }
    let mut bad = config.clone();
    bad.initial_state = Some(vec![]);
    assert!(
        audit::configured_step(
            &circuit,
            &result.samples[0],
            &result.samples[1],
            &bad,
            AuditOptions::default()
        )
        .is_err()
    );
    let options = AuditOptions {
        relative: f64::NAN,
        ..AuditOptions::default()
    };
    assert!(
        audit::configured_step(
            &circuit,
            &result.samples[0],
            &result.samples[1],
            &config,
            options
        )
        .is_err()
    );
}

proptest::proptest! {
    #![proptest_config(proptest::test_runner::Config {cases:32,
        rng_seed:proptest::test_runner::RngSeed::Fixed(2026103006), ..Default::default()})]
    #[test]
    fn seeded_rc_physical_checks(ohms in 10_f64..100000., farads in 1e-9_f64..1e-4,
        real in -3_f64..3., imag in -3_f64..3., frequency in 1_f64..10000.) {
        let circuit=parse(&format!("RC\nV1 in 0 1\nR1 in out {ohms}\nC1 out 0 {farads}\n.end")).unwrap();
        let plan=compile(&circuit).unwrap();
        let phasors=[AcSourcePhasor{id:"V1".into(),real,imag}];
        let sample=ac_with_source_phasors(&plan,&[frequency],&phasors,&FaerSparseLu).unwrap().remove(0);
        proptest::prop_assert!(!audit::ac(&circuit,&sample,&phasors,AuditOptions::default()).unwrap().has_failures());
        for method in [Method::BackwardEuler,Method::Trapezoidal] {
            let h=ohms*farads/16.;
            let config=TransientConfig{stop_s:4.*h,max_step_s:h,max_steps:10,method,
                initial_state:Some(vec![1.,0.,-1./ohms]),sources:vec![],adaptive:None};
            let result=run(&plan,&FaerSparseLu,&config,&false.into()).unwrap();
            for pair in result.samples.windows(2) {
                proptest::prop_assert!(!audit::configured_step(&circuit,&pair[0],&pair[1],&config,AuditOptions::default()).unwrap().has_failures());
            }
        }
    }
}
