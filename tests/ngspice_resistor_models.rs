//! Named passive models reuse the resistor equation without discarding physics.
use circuit_simulator::{circuit::Element, netlist::import_ngspice_request};
use proptest::{
    prelude::*,
    test_runner::{Config, RngSeed},
};

fn resistance(model: &str) -> circuit_simulator::error::SimResult<f64> {
    let request = import_ngspice_request(
        &format!("owned\nV1 out 0 1\nR1 out 0 rm\n{model}\n.op\n.end"),
        "res-model",
    )?;
    Ok(request
        .circuit
        .elements
        .iter()
        .find_map(|e| {
            if let Element::Resistor { ohms, .. } = e {
                Some(*ohms)
            } else {
                None
            }
        })
        .unwrap())
}

#[test]
fn named_resistance_is_case_insensitive_and_forward_referenced() {
    assert_eq!(resistance(".MoDeL RM R (R = 2k)").unwrap(), 2000.);
    assert_eq!(resistance(".model rm R R=1k").unwrap(), 1000.);
}

#[test]
fn unsupported_physics_and_ambiguous_models_fail_closed() {
    for model in [
        ".model rm R",
        ".model rm R (R=-1)",
        ".model rm R (R=0)",
        ".model rm R (R=NaN)",
        ".model rm R (R=1k TC1=0.01)",
        ".model rm R (RSH=100)",
        ".model rm R (R=1k R=2k)",
        ".model rm R (R=1k)\n.model RM D",
        ".model rm R (R=1k)\n.model RM R (R=2k)",
        ".model rm NPN (BF=100)",
    ] {
        assert!(resistance(model).is_err(), "{model}");
    }
}

proptest! {
    #![proptest_config(Config {cases:128, rng_seed:RngSeed::Fixed(0x5350494345524d44), ..Config::default()})]
    #[test]
    fn model_resistance_preserves_ohms(value in 1u32..100000) {
        prop_assert_eq!(resistance(&format!(".model rm R (R={value})")).unwrap(), f64::from(value));
    }
}
