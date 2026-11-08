use circuit_simulator::{circuit::Circuit, compile::compile, newton::linearize};
use proptest::prelude::*;
use proptest::test_runner::{Config, RngSeed, TestRunner};

const CARD: &str = r#"{
    "nodes":["0","d","g","s","b"],
    "elements":[{"kind":"bsim330","id":"m1","p":1,"gate":2,"n":3,"bulk":4,
    "model":{"polarity":1,"tox":3.5e-9,"nch":2.4e17,"nsub":5e16,"vth0":0.15,
    "w":1e-6,"l":3.5e-7,"pd":1.5e-6,"ps":1.5e-6,"ad":1.5e-12,"as":1.5e-12}}]
}"#;

#[test]
fn bsim330_rejects_invalid_geometry_before_native_work() {
    let circuit: Circuit = serde_json::from_str(&CARD.replace("1e-6", "-1e-6")).unwrap();
    assert!(compile(&circuit).is_err());
}

#[test]
#[ignore = "requires the pinned Windows native BSIM3 host and model"]
fn bsim330_trial_stamps_four_terminal_current_and_jacobian() {
    let circuit: Circuit = serde_json::from_str(CARD).unwrap();
    let plan = compile(&circuit).unwrap();
    let trial = linearize(&plan, &[0.8, 0.8, 0.0, 0.0]).unwrap();
    assert!((trial.residual[0] - 7.34294748e-5).abs() < 2e-12);
    assert!(trial.residual.iter().sum::<f64>().abs() < 1e-15);
    assert!((trial.jacobian.values[1] - 2.622e-4).abs() < 1e-7);

    let config = Config {
        cases: 64,
        rng_seed: RngSeed::Fixed(330),
        failure_persistence: None,
        ..Config::default()
    };
    TestRunner::new(config)
        .run(&(0.0..2.0, 0.0..2.0, -0.5..0.0), |(drain, gate, bulk)| {
            let f = linearize(&plan, &[drain, gate, 0.0, bulk]).unwrap();
            prop_assert!(f.residual.iter().sum::<f64>().abs() < 1e-15);
            let charge =
                circuit_simulator::newton::charge_at_bias_csc(&plan, &[drain, gate, 0.0, bulk])
                    .unwrap();
            let mut column_sums = [0.0; 4];
            for (_, column, value) in charge.entries() {
                column_sums[column] += value;
            }
            prop_assert!(column_sums.into_iter().all(|sum| sum.abs() < 1e-22));
            for col in 0..4 {
                let sum = (0..4)
                    .map(|row| f.jacobian.values[row * 4 + col])
                    .sum::<f64>();
                prop_assert!(sum.abs() < 1e-10);
            }
            Ok(())
        })
        .unwrap();
}

#[test]
#[ignore = "requires the pinned Windows native BSIM3 host and model"]
fn bsim330_ac_charge_and_backward_euler_history() {
    use circuit_simulator::newton::{
        charge_at_bias_csc, conductance_at_bias_csc, linearize_backward_euler,
    };
    let circuit: Circuit = serde_json::from_str(CARD).unwrap();
    let plan = compile(&circuit).unwrap();
    let old = [0.8, 0.7999, 0.0, 0.0];
    let bias = [0.8, 0.8001, 0.0, 0.0];
    let dc = linearize(&plan, &bias).unwrap();
    let conductance = conductance_at_bias_csc(&plan, &bias).unwrap();
    let gm = conductance
        .entries()
        .find(|(row, col, _)| (*row, *col) == (0, 1))
        .unwrap()
        .2;
    assert!((gm - dc.jacobian.values[1]).abs() < 1e-12);
    let charge = charge_at_bias_csc(&plan, &bias).unwrap();
    let cgg = charge
        .entries()
        .find(|(row, col, _)| (*row, *col) == (1, 1))
        .unwrap()
        .2;
    // Independent ngspice BSIM3 3.3.0 gate charges at 0.7999 and 0.8001 V.
    let delta_q = 3.861555817043642e-15 - 3.860642374545776e-15;
    assert!((cgg - delta_q / 0.0002).abs() < 2e-18);
    let h = 1e-10;
    let step = linearize_backward_euler(&plan, &bias, &old, h, &[0.; 4]).unwrap();
    assert!((step.residual[1] - dc.residual[1] - delta_q / h).abs() < 2e-13);
    assert!((step.jacobian.values[5] - dc.jacobian.values[5] - cgg / h).abs() < 1e-12);
    assert!(step.residual.iter().sum::<f64>().abs() < 1e-15);
    let _discard = linearize_backward_euler(&plan, &old, &old, h / 2., &[0.; 4]).unwrap();
    let repeat = linearize_backward_euler(&plan, &bias, &old, h, &[0.; 4]).unwrap();
    assert_eq!(step.residual, repeat.residual);
}
