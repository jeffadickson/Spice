//! Independent discrete RC identity through the nonlinear step-doubling branch.
use circuit_simulator::{
    backend::DenseLu,
    circuit::{Circuit, Element},
    compile::compile,
    configured::{TransientConfig, run},
    ids::NodeId,
};

#[test]
fn nonlinear_adaptive_first_half_step_uses_half_the_attempted_interval() {
    let circuit = Circuit {
        nodes: vec!["0".into(), "in".into(), "out".into()],
        elements: vec![
            Element::Voltage {
                id: "V1".into(),
                p: NodeId(1),
                n: NodeId(0),
                volts: 1.,
            },
            Element::Resistor {
                id: "R1".into(),
                p: NodeId(1),
                n: NodeId(2),
                ohms: 1.,
            },
            Element::DiodeWithCapacitance {
                id: "D1".into(),
                p: NodeId(2),
                n: NodeId(0),
                saturation_amps: 1e-20,
                thermal_volts: 0.025,
                farads: 1.,
            },
        ],
    };
    let plan = compile(&circuit).unwrap();
    assert!(plan.has_nonlinear_devices());
    let h = 0.25_f64;
    // The controller tolerance deliberately admits one known attempted step.
    // Accuracy below is checked tightly against the discrete equation, not this
    // loose LTE setting, and is not a claim about global waveform accuracy.
    // JSON uses the common options supported by both pinned source revisions.
    let adaptive = serde_json::from_value(serde_json::json!({"relative":0.9,
        "voltage_absolute":0.001,"current_absolute":0.001,
        "min_step_s":0.01,"max_attempts":5}))
    .unwrap();
    let config = TransientConfig {
        stop_s: h,
        max_step_s: h,
        max_steps: 2,
        initial_state: Some(vec![1., 0., -1.]),
        adaptive: Some(adaptive),
        ..Default::default()
    };
    let result = run(&plan, &DenseLu, &config, &false.into()).unwrap();
    assert_eq!(
        result.samples.len(),
        2,
        "fixture must admit exactly one full attempted interval"
    );
    let last = result.samples.last().unwrap();
    assert_eq!(last.time_s, h);
    // RC=1 s. Two backward-Euler half steps have closed-form voltage
    // 1-(1+h/2)^(-2). At this bias the extra Shockley current is below 5e-17 A,
    // far below the 1e-11 V check (R=1 ohm), while dispatch remains nonlinear.
    let expected = 1. - (1. + h / 2.).powi(-2);
    assert!(
        (last.values[1] - expected).abs() < 1e-11,
        "{} vs {expected}",
        last.values[1]
    );
    assert!((last.values[2] + (1. - expected)).abs() < 1e-11);
}
