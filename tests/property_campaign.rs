//! Library-generated cases with shrinking, persisted counterexamples and replay.
use circuit_simulator::newton::norm_inf;
use circuit_simulator::{
    api::Request,
    backend::{DenseLu, DenseQr},
    backend_faer::FaerSparseLu,
    compile::compile,
    dc::operating_point,
    linalg::LinearSolver,
    netlist::{import_ngspice_request, parse},
};
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

    #[test]
    fn malformed_parser_inputs_reject_without_panicking(bytes in prop::collection::vec(any::<u8>(), 0..2048)) {
        let text = String::from_utf8_lossy(&bytes);
        let _ = parse(&text);
        let _ = import_ngspice_request(&text, "proptest");
        if let Ok(request) = serde_json::from_slice::<Request>(&bytes) {
            let _ = request.circuit.validate();
        }
        // A rejected input is expected. Only panic/resource-contract failures
        // fail this property; acceptance does not assert numerical correctness.
    }

    #[test]
    fn unicode_netlist_tokens_have_total_diagnostics(identifier in ".{0,128}") {
        let text = format!("title\n{identifier} a 0 1\n.end\n");
        let _ = parse(&text);
        let _ = import_ngspice_request(&text, "unicode-proptest");
    }

    #[test]
    fn generated_dividers_match_analytical_voltage_and_current(
        exponent1 in -1.0_f64..6., exponent2 in -1.0_f64..6., volts in -100.0_f64..100.
    ) {
        let r1=10_f64.powf(exponent1);
        let r2=10_f64.powf(exponent2);
        let circuit=parse(&format!("title\nV1 a 0 {volts}\nR1 a b {r1}\nR2 b 0 {r2}\n.end\n")).unwrap();
        let plan=compile(&circuit).unwrap();
        let current=volts/(r1+r2);
        let expected=current*r2;
        for provider in [&DenseLu as &dyn LinearSolver, &DenseQr, &FaerSparseLu] {
            let values=operating_point(&plan,provider).unwrap();
            prop_assert!((values[1]-expected).abs() < 1e-7);
            prop_assert!((values[2]+current).abs() < 1e-7*(1.+current.abs()));
            let upper=(values[0]-values[1])/r1;
            let lower=values[1]/r2;
            prop_assert!((upper-lower).abs() < 1e-7*(1.+current.abs()));
        }
    }
}
