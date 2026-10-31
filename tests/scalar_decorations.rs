//! Finite bounded suffix compatibility; decorations are not dimensional typing.
use circuit_simulator::quantity::spice_scalar;
use proptest::prelude::*;
use proptest::test_runner::{Config, RngSeed};

proptest! {
    #![proptest_config(Config { cases: 512, rng_seed: RngSeed::Fixed(0x5350494345535546), .. Config::default() })]
    #[test]
    fn decorated_integer_values_preserve_the_analytical_scale(value in -1000000i32..1000000,
        index in 0usize..4, upper in any::<bool>()) {
        let (suffix, scale)=[("ns",1e-9),("ps",1e-12),("khz",1e3),("s",1.0)][index];
        let suffix=if upper {suffix.to_uppercase()} else {suffix.to_owned()};
        let actual=spice_scalar(&format!("{value}{suffix}")).unwrap();
        let expected=f64::from(value)*scale;
        prop_assert!((actual-expected).abs()<=expected.abs()*1e-14);
    }
}

#[test]
fn bounded_time_and_frequency_decorations_keep_spice_scale_conventions() {
    for (text, expected) in [
        ("1ps", 1e-12),
        ("1NS", 1e-9),
        (".01s", 0.01),
        ("0.05MS", 5e-5),
        ("100kHz", 1e5),
        ("10ghz", 1e10),
        ("1MEGHz", 1e6),
        ("-2e-3us", -2e-9),
        ("1m", 1e-3),
    ] {
        let actual = spice_scalar(text).unwrap();
        assert!(
            (actual - expected).abs() <= expected.abs() * 1e-14,
            "{text}: {actual}"
        );
    }
}

#[test]
fn decorations_do_not_admit_expressions_unknown_tails_or_nonfinite_values() {
    for text in [
        "s", "Hz", "1ss", "1hzz", "1junk", "1e+ns", "1ns+2", "1e309s", "NaNs", "infs", "1e308Ts",
    ] {
        assert!(spice_scalar(text).is_err(), "accepted {text}");
    }
}
