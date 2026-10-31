//! Optional transient fields must not erase requested simulation semantics.
use circuit_simulator::{api::Analysis, netlist::import_ngspice_request};
use proptest::{
    prelude::*,
    test_runner::{Config, RngSeed},
};

fn deck(card: &str) -> String {
    format!("owned\nV1 out 0 1\nR1 out 0 1000\n{card}\n.end")
}

#[test]
fn zero_start_and_maximum_step_lower_explicitly() {
    for (card, expected) in [(".tran 1m 5m 0", 0.001), (".tran 1m 5m 0 250u", 0.00025)] {
        let request = import_ngspice_request(&deck(card), "optional-tran").unwrap();
        let Analysis::TransientConfigured { options } = request.analysis else {
            panic!("transient expected")
        };
        assert_eq!(options.stop_s, 0.005);
        assert_eq!(options.max_step_s, expected);
    }
}

#[test]
fn unsupported_or_ambiguous_cards_are_not_silently_selected() {
    for card in [
        ".tran 1m 5m 1m",
        ".tran 1m 5m uic",
        ".tran 1m 5m 0 0",
        ".tran 1m 5m 0 -1m",
        ".tran 1m 5m 0 NaN",
        ".tran 0 5m 0 1m",
        ".tran 1m 5m\n.tran 1m 5m",
        ".op\n.op",
        ".ac lin 1 1 1\n.ac lin 1 1 1",
        ".tran 1m 5m\n.ac lin 1 1 1",
    ] {
        assert!(
            import_ngspice_request(&deck(card), "bad-analysis").is_err(),
            "{card}"
        );
    }
}

#[test]
fn recognized_uic_is_unsupported_initialization_not_a_bad_number() {
    let error = import_ngspice_request(&deck(".tran 1m 5m uic"), "uic").unwrap_err();
    assert_eq!(error.code, circuit_simulator::error::ErrorCode::Unsupported);
}

proptest! {
    #![proptest_config(Config {cases: 128, rng_seed: RngSeed::Fixed(0x535049434554524e), ..Config::default()})]
    #[test]
    fn explicit_step_is_never_lost(microseconds in 1u32..1000, stop_ms in 2u32..20) {
        let request=import_ngspice_request(&deck(&format!(".tran 1m {stop_ms}m 0 {microseconds}u")), "property").unwrap();
        let Analysis::TransientConfigured {options}=request.analysis else {panic!("transient expected")};
        prop_assert!((options.max_step_s - f64::from(microseconds)*1e-6).abs()<1e-18);
        // Vary stop independently of TransientConfig's default; otherwise a
        // deleted stop assignment can be masked by an accidentally equal case.
        prop_assert_eq!(options.stop_s, f64::from(stop_ms)*1e-3);
    }
}
