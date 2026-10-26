//! Library-generated cases with shrinking, persisted counterexamples and replay.
use circuit_simulator::newton::norm_inf;
use proptest::prelude::*;
use proptest::test_runner::{Config, FileFailurePersistence, RngSeed};

fn configuration() -> Config {
    let mut config = Config::default();
    // Honor Proptest's documented seed/case environment controls; a stable
    // default makes checkpoint failures reproducible on a second computer.
    if std::env::var_os("PROPTEST_RNG_SEED").is_none() {
        config.rng_seed = RngSeed::Fixed(0x5350_4943_4533_3535);
    }
    config.failure_persistence = Some(Box::new(FileFailurePersistence::Direct(
        "tests/regressions/property_campaign.txt",
    )));
    config
}

proptest! {
    #![proptest_config(configuration())]
    #[test]
    fn norm_obeys_finite_ieee_contract(bits in prop::collection::vec(any::<u64>(), 0..256)) {
        let values: Vec<_> = bits.into_iter().map(f64::from_bits).collect();
        let result = norm_inf(&values);
        if values.iter().any(|v| !v.is_finite()) {
            prop_assert!(result.is_err());
        } else {
            let expected = values.iter().map(|v| v.abs()).fold(0., f64::max);
            prop_assert_eq!(result.unwrap(), expected);
        }
    }
}
