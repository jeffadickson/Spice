use circuit_simulator::{
    adaptive::AdaptiveOptions,
    api::{Request, run},
    backend::DenseLu,
    compile::compile,
    configured::{self, Method, TransientConfig},
    netlist::parse,
    waveform::Waveform,
};
fn config(relative: f64) -> TransientConfig {
    TransientConfig {
        stop_s: 0.005,
        max_step_s: 0.001,
        method: Method::Trapezoidal,
        initial_state: Some(vec![1.]),
        adaptive: Some(AdaptiveOptions { current_relative: None,
            relative,
            voltage_absolute: 1e-10,
            current_absolute: 1e-12,
            min_step_s: 1e-9,
            max_attempts: 100000,
        }),
        ..Default::default()
    }
}
#[test]
fn tighter_local_tolerance_improves_rc_reference() {
    let p = compile(&parse("title\nR1 a 0 1k\nC1 a 0 1u").unwrap()).unwrap();
    let loose = configured::run(&p, &DenseLu, &config(1e-3), &false.into()).unwrap();
    let tight = configured::run(&p, &DenseLu, &config(1e-6), &false.into()).unwrap();
    let truth = (-5_f64).exp();
    let error =
        |r: &configured::ConfiguredResult| (r.samples.last().unwrap().values[0] - truth).abs();
    assert!(error(&tight) < error(&loose));
    assert!(error(&tight) < 1e-5);
    assert!(tight.report.rejected_steps > 0);
    assert!(tight.report.max_accepted_error <= 1.);
    assert!(tight.samples.windows(2).all(|w| w[1].time_s > w[0].time_s));
}
#[test]
fn observers_retain_only_accepted_samples_before_cancel() {
    let p = compile(&parse("title\nR1 a 0 1k\nC1 a 0 1u").unwrap()).unwrap();
    let cancel = std::sync::atomic::AtomicBool::new(false);
    let mut observed = Vec::new();
    let result = configured::run_observed(&p, &DenseLu, &config(1e-6), &cancel, |s| {
        observed.push(s.time_s);
        if observed.len() == 4 {
            cancel.store(true, std::sync::atomic::Ordering::Relaxed);
        }
        Ok(())
    });
    assert!(result.is_err());
    assert_eq!(observed.len(), 4);
    assert!(observed.windows(2).all(|w| w[1] > w[0]));
}
#[test]
fn prepared_factors_are_reused_in_fixed_linear_run() {
    let p = compile(&parse("title\nR1 a 0 1k\nC1 a 0 1u").unwrap()).unwrap();
    let c = TransientConfig {
        initial_state: Some(vec![1.]),
        ..Default::default()
    };
    let r = configured::run(&p, &DenseLu, &c, &false.into()).unwrap();
    assert!(r.report.prepared_reuses > 0);
    assert!(r.report.prepared_factors < r.report.linear_solves);
}
#[test]
fn integrated_measurements_and_assertions_are_machine_readable() {
    let request: Request =
        serde_json::from_str(include_str!("../examples/adaptive-rc.json")).unwrap();
    let r = run(&request).unwrap();
    assert_eq!(r.measurements[0].status, "valid");
    assert_eq!(
        r.assertions[0].outcome,
        circuit_simulator::assertion::Outcome::Pass
    );
    assert!(r.integration.unwrap().rejected_steps > 0);
}
#[test]
fn sine_and_invalid_adaptive_budget() {
    let s = Waveform::Sine {
        offset: 2.,
        amplitude: 3.,
        frequency_hz: 1.,
        phase_rad: 0.,
    };
    assert!((s.at(0.25).unwrap() - 5.).abs() < 1e-12);
    assert!(
        AdaptiveOptions { current_relative: None,
            relative: 0.,
            voltage_absolute: 1e-6,
            current_absolute: 1e-9,
            min_step_s: 1e-12,
            max_attempts: 10
        }
        .validate(0.01)
        .is_err()
    );
}
