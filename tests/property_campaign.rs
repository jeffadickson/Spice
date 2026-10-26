//! Library-generated cases with shrinking, persisted counterexamples and replay.
use circuit_simulator::model::DiodeModel;
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
use circuit_simulator::{linalg::Matrix, sparse::CscMatrix};
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
    fn generated_rc_ac_matches_complex_transfer(
        resistance_log in 0.0_f64..5., capacitance_log in -9.0_f64..-4.,
        ratio_log in -3.0_f64..3., real in -10.0_f64..10., imag in -10.0_f64..10.
    ) {
        use circuit_simulator::{ac::ac_with_source_phasors, api::AcSourcePhasor};
        use num_complex::Complex64;
        let resistance = 10_f64.powf(resistance_log);
        let capacitance = 10_f64.powf(capacitance_log);
        let ratio = 10_f64.powf(ratio_log);
        let frequency = ratio / (std::f64::consts::TAU * resistance * capacitance);
        let plan = compile(&parse(&format!("title\nV1 in 0 1\nR1 in out {resistance}\nC1 out 0 {capacitance}\n.end")).unwrap()).unwrap();
        let source = AcSourcePhasor { id: "V1".into(), real, imag };
        let expected = Complex64::new(real, imag) / Complex64::new(1., ratio);
        for provider in [&DenseLu as &dyn LinearSolver, &FaerSparseLu] {
            let samples = ac_with_source_phasors(&plan, &[frequency], std::slice::from_ref(&source), provider).unwrap();
            let actual = Complex64::new(samples[0].values[1].real, samples[0].values[1].imag);
            prop_assert!((actual - expected).norm() < 1e-10 * (1. + expected.norm()));
        }
    }
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

    #[test]
    fn canonical_sparse_products_match_independent_dense_products(
        (n, coefficients) in (1_usize..12).prop_flat_map(|n| (Just(n), prop::collection::vec(-10_i16..10, n*n)))
    ) {
        let values: Vec<_> = coefficients.into_iter().map(f64::from).collect();
        let matrix=Matrix { n, values };
        let sparse=CscMatrix::from_dense(&matrix).unwrap();
        let mut actual=vec![0.;n];
        for (row,column,value) in sparse.entries() {
            actual[row]+=value*(column+1) as f64;
        }
        for (row, observed) in actual.iter().enumerate() {
            let expected: f64=(0..n).map(|column| matrix.values[row*n+column]*(column+1) as f64).sum();
            prop_assert_eq!(*observed,expected);
        }
    }

    #[test]
    fn arbitrary_csc_boundaries_never_expose_invalid_slots(
        n in 0_usize..260,
        pointers in prop::collection::vec(any::<usize>(),0..64),
        rows in prop::collection::vec(any::<usize>(),0..64),
        bits in prop::collection::vec(any::<u64>(),0..64)
    ) {
        let values=bits.into_iter().map(f64::from_bits).collect();
        if let Ok(matrix)=CscMatrix::new(n,pointers,rows,values) {
            prop_assert_eq!(matrix.entries().count(),matrix.nnz());
            prop_assert!(matrix.entries().all(|(row,column,value)| row<n && column<n && value.is_finite()));
        }
    }

    #[test]
    fn diode_jacobians_match_central_differences_over_generated_si_parameters(
        log_isat in -15.0_f64..-8., log_vt in -5.0_f64..-0.3, exponent in -20.0_f64..20.
    ) {
        let vt=10_f64.powf(log_vt);
        let model=DiodeModel{saturation_amps:10_f64.powf(log_isat),thermal_volts:vt};
        let voltage=vt*exponent;
        let h=vt*1e-4;
        let analytic=model.linearize(voltage).unwrap().conductance_siemens;
        let plus=model.linearize(voltage+h).unwrap().current_amps;
        let minus=model.linearize(voltage-h).unwrap().current_amps;
        let numerical=(plus-minus)/(2.*h);
        // In deep reverse bias, differencing two currents near -Is loses
        // significance. Bound that oracle's rounding amplification explicitly;
        // this does not alter any production solver acceptance tolerance.
        let roundoff=16.*f64::EPSILON*(plus.abs()+minus.abs())/(2.*h);
        prop_assert!((analytic-numerical).abs() <= 1e-7*analytic+roundoff+1e-30);
        prop_assert!(analytic>0.);
    }

    #[test]
    fn diode_near_zero_current_retains_its_differential_limit(log_exponent in -20.0_f64..-4., negative in any::<bool>()) {
        let x=10_f64.powf(log_exponent)*if negative {-1.}else{1.};
        let model=DiodeModel{saturation_amps:1e-12,thermal_volts:0.025};
        let current=model.linearize(x*model.thermal_volts).unwrap().current_amps;
        // Taylor's theorem bounds exp(x)-1 relative to x for |x|<=1e-4.
        // This independent small-signal bound detects exp(x)-1 cancellation.
        prop_assert!((current/(model.saturation_amps*x)-1.).abs() <= x.abs()+1e-14);
    }
}
