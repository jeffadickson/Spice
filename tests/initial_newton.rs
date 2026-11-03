//! Initial nonlinear bias is independently controlled and observable. These
//! tests use inverse Shockley physics, not the Newton residual implementation.
use circuit_simulator::{
    backend::DenseLu,
    circuit::{Circuit, Element},
    compile::compile,
    configured::{TransientConfig, run},
    ids::NodeId,
    newton::{ContinuationOptions, NewtonOptions},
};
use std::sync::atomic::{AtomicUsize, Ordering};

// Count the actual supplied provider, including initialization. No alternative
// numerical provider can satisfy these accounting assertions unnoticed.
struct RecordingLu(AtomicUsize);
impl circuit_simulator::linalg::LinearSolver for RecordingLu {
    fn name(&self) -> &'static str { "initial_bias_recording_lu" }
    fn solve(&self, matrix:&circuit_simulator::linalg::Matrix, rhs:&[f64])
        -> circuit_simulator::error::SimResult<Vec<f64>> {
        self.0.fetch_add(1,Ordering::SeqCst);
        circuit_simulator::linalg::LinearSolver::solve(&DenseLu,matrix,rhs)
    }
}

fn diode() -> Circuit {
    Circuit {
        nodes: vec!["0".into(), "bias".into()],
        elements: vec![
            Element::Current {
                id: "I1".into(),
                p: NodeId(0),
                n: NodeId(1),
                amps: 1e-6,
            },
            Element::Diode {
                id: "D1".into(),
                p: NodeId(1),
                n: NodeId(0),
                saturation_amps: 1e-15,
                thermal_volts: 0.02585,
            },
        ],
    }
}

fn configuration(policy: NewtonOptions) -> TransientConfig {
    serde_json::from_value(serde_json::json!({
        "stop_s":1e-6,"max_step_s":1e-6,"max_steps":2,
        "initial_newton":policy
    }))
    .expect("Explicit initial Newton policy must be recognized")
}

#[test]
fn explicit_initial_iteration_budget_is_not_silently_ignored() {
    let plan = compile(&diode()).unwrap();
    let policy = NewtonOptions {
        max_iterations: 1,
        ..Default::default()
    };
    let result = run(&plan, &DenseLu, &configuration(policy), &false.into());
    assert!(
        result.is_err(),
        "One initial Newton iteration cannot solve this bias"
    );
}

#[test]
fn source_initialization_recovers_nominal_bias_with_an_unchanged_step_budget() {
    let plan = compile(&diode()).unwrap();
    let policy = NewtonOptions {
        max_iterations: 8,
        continuation: Some(ContinuationOptions {
            minimum_step: 1e-12,
            ..Default::default()
        }),
        ..Default::default()
    };
    let provider=RecordingLu(AtomicUsize::new(0));
    let result = run(&plan, &provider, &configuration(policy), &false.into()).unwrap();
    assert_eq!(provider.0.load(Ordering::SeqCst),result.report.linear_solves);
    assert!(result.report.initial_newton.as_ref().unwrap().linear_solves>1);
    assert_eq!(result.samples.len(), 2);
    assert_eq!(result.report.accepted_steps, 1);
    let evidence = serde_json::to_value(&result.report).unwrap();
    let stages = evidence["initial_newton"]["continuation"]["stages"]
        .as_array()
        .expect("Initial source recovery evidence must be retained");
    assert_eq!(stages.last().unwrap()["source_scale"].as_f64(), Some(1.0));
    let expected = 0.02585_f64 * (1.0_f64 + 1e-6 / 1e-15).ln();
    for sample in &result.samples {
        assert!((sample.values[0] - expected).abs() < 2e-7);
        assert!((1e-15 * (sample.values[0] / 0.02585).exp_m1() - 1e-6).abs() < 2e-12);
    }
}

#[test]
fn one_iteration_initial_policy_does_not_leak_into_changing_source_steps() {
    use circuit_simulator::{sources::SourceOverride,waveform::{Point,Waveform}};
    let mut circuit=diode();
    circuit.elements[0]=Element::Current {id:"I1".into(),p:NodeId(0),n:NodeId(1),amps:0.};
    let plan=compile(&circuit).unwrap();
    let mut options=configuration(NewtonOptions {max_iterations:1,..Default::default()});
    options.stop_s=2e-6;
    options.sources=vec![SourceOverride {id:"I1".into(),waveform:Waveform::Pwl {points:vec![
        Point{time_s:0.,value:0.},Point{time_s:1e-6,value:1e-6},Point{time_s:2e-6,value:2e-6}
    ]}}];
    let provider=RecordingLu(AtomicUsize::new(0));
    let result=run(&plan,&provider,&options,&false.into()).unwrap();
    assert_eq!(result.report.accepted_steps,2);
    assert_eq!(result.report.initial_newton.as_ref().unwrap().linear_solves,0);
    assert!(provider.0.load(Ordering::SeqCst)>2,"Later steps require multiple Newton corrections");
    assert_eq!(provider.0.load(Ordering::SeqCst),result.report.linear_solves);
    for (index,sample) in result.samples.iter().enumerate() {
        let expected=0.02585*(1.0+index as f64*1e-6/1e-15).ln();
        assert!((sample.values[0]-expected).abs()<2e-7);
    }
}

#[test]
fn explicit_state_and_initial_newton_are_mutually_exclusive() {
    let mut options = configuration(NewtonOptions::default());
    options.initial_state = Some(vec![0.]);
    assert!(options.validate().is_err());
}

#[test]
fn fixed_initial_gmin_cannot_leave_a_nonphysical_bias() {
    let options = configuration(NewtonOptions {
        gmin_siemens: Some(1e-3),
        ..Default::default()
    });
    assert!(options.validate().is_err());
}

#[test]
fn linear_initialization_refuses_an_inapplicable_nonlinear_policy() {
    let mut circuit = diode();
    circuit.elements[1] = Element::Resistor {
        id: "R1".into(),
        p: NodeId(1),
        n: NodeId(0),
        ohms: 1e3,
    };
    let plan = compile(&circuit).unwrap();
    let options = configuration(NewtonOptions::default());
    let error = run(&plan, &DenseLu, &options, &false.into()).err().unwrap();
    assert_eq!(error.code, circuit_simulator::error::ErrorCode::Unsupported);
    let legacy = run(
        &plan,
        &DenseLu,
        &TransientConfig {
            initial_newton: None,
            ..options
        },
        &false.into(),
    )
    .unwrap();
    assert!(legacy.report.initial_newton.is_none());
    assert!((legacy.samples[0].values[0] - 0.001).abs() < 1e-12);
}

#[test]
fn malformed_initial_policies_are_rejected() {
    let mut policy = NewtonOptions::default();
    policy.max_iterations = 0;
    assert!(configuration(policy).validate().is_err());
    assert!(
        serde_json::from_value::<TransientConfig>(serde_json::json!({
            "stop_s":1e-6,"max_step_s":1e-6,"max_steps":2,
            "initial_newton":{"invented_policy":true}
        }))
        .is_err()
    );
}

#[test]
fn staged_gmin_initialization_removes_the_shunt_before_storing_state() {
    let plan = compile(&diode()).unwrap();
    let policy = NewtonOptions {
        gmin_continuation: Some(Default::default()),
        ..Default::default()
    };
    let result = run(&plan, &DenseLu, &configuration(policy), &false.into()).unwrap();
    let report = result.report.initial_newton.as_ref().unwrap();
    assert!(report.gmin_continuation.as_ref().unwrap().final_unregularized);
    assert!(report.gmin_siemens.is_none());
    let expected = 0.02585_f64 * (1.0_f64 + 1e-6 / 1e-15).ln();
    for sample in &result.samples {
        assert!((sample.values[0] - expected).abs() < 2e-7);
    }
}

proptest::proptest! {
    #![proptest_config(proptest::test_runner::Config::with_cases(64))]
    #[test]
    fn recovered_bias_obeys_inverse_shockley_across_current_scales(exponent in -8.0_f64..-4.0_f64) {
        let amps=10_f64.powf(exponent);
        let mut circuit=diode();
        circuit.elements[0]=Element::Current {id:"I1".into(),p:NodeId(0),n:NodeId(1),amps};
        let plan=compile(&circuit).unwrap();
        let options=configuration(NewtonOptions {continuation:Some(Default::default()),..Default::default()});
        let result=run(&plan,&DenseLu,&options,&false.into()).unwrap();
        let expected=0.02585*(1.0+amps/1e-15).ln();
        proptest::prop_assert!((result.samples[0].values[0]-expected).abs()<3e-6);
        proptest::prop_assert_eq!(result.report.accepted_steps,1);
    }
}

#[test]
fn continuation_never_substitutes_nominal_sources_for_a_different_time_zero_rhs() {
    let plan = compile(&diode()).unwrap();
    let mut options = configuration(NewtonOptions {
        continuation: Some(Default::default()),
        ..Default::default()
    });
    options.sources = vec![circuit_simulator::sources::SourceOverride {
        id: "I1".into(),
        waveform: circuit_simulator::waveform::Waveform::Constant { value: 2e-6 },
    }];
    let error = run(&plan, &DenseLu, &options, &false.into())
        .err()
        .expect("Different excitation must refuse continuation");
    assert_eq!(error.code, circuit_simulator::error::ErrorCode::Unsupported);
    assert!(error.message.contains("nominal time-zero"));
    // Direct bias policy still honors the actual override as before.
    let mut direct = configuration(NewtonOptions::default());
    direct.sources = options.sources;
    let result = run(&plan, &DenseLu, &direct, &false.into()).unwrap();
    let expected = 0.02585_f64 * (1.0_f64 + 2e-6 / 1e-15).ln();
    assert!((result.samples[0].values[0] - expected).abs() < 2e-7);
}
