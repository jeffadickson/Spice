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
    fn delayed_sine_holds_phase_bias_before_delay(
        offset in -10.0_f64..10., amplitude in 0.0_f64..10.,
        phase in -6.0_f64..6., delay in 0.001_f64..10., fraction in 0.0_f64..1.
    ) {
        use circuit_simulator::waveform::Waveform;
        let wave=Waveform::DampedSine{offset,amplitude,frequency_hz:10.,delay_s:delay,damping_per_s:2.,phase_rad:phase};
        // Independent ngspice source law: phase-biased initial value is held
        // before TD. Frequency/damping cannot affect this pre-delay interval.
        let expected=offset+amplitude*phase.sin();
        prop_assert!((wave.at(delay*fraction).unwrap()-expected).abs()<1e-12);
        prop_assert!((wave.at(0.).unwrap()-expected).abs()<1e-12);
    }

    #[test]
    fn midpoint_switch_divider_matches_geometric_resistance_on_both_cpu_providers(
        ron_log in 1.0_f64..3., roff_log in 4.0_f64..5.,
        load_log in 2.0_f64..4., drive in -2.0_f64..2., reverse in any::<bool>()
    ) {
        use circuit_simulator::{circuit::{Circuit,Element},ids::NodeId as N};
        let ron=10_f64.powf(ron_log);let roff=10_f64.powf(roff_log);let load=10_f64.powf(load_log);
        let circuit=Circuit{nodes:vec!["0".into(),"in".into(),"out".into(),"control".into()],elements:vec![
            Element::Voltage{id:"drive".into(),p:N(1),n:N(0),volts:drive},
            Element::Voltage{id:"control".into(),p:N(3),n:N(0),volts:0.5},
            Element::PspiceSwitch{id:"switch".into(),p:N(1),n:N(2),cp:N(3),cn:N(0),ron,roff,von:if reverse{0.}else{1.},voff:if reverse{1.}else{0.}},
            Element::Resistor{id:"load".into(),p:N(2),n:N(0),ohms:load}
        ]};
        let plan=compile(&circuit).unwrap();
        // At the center of the smooth transition the resistance is the
        // geometric mean; use an independent series-divider reference.
        let expected=drive*load/(load+(ron*roff).sqrt());
        for solver in [&DenseLu as &dyn LinearSolver,&FaerSparseLu] {
            let state=operating_point(&plan,solver).unwrap();
            prop_assert!((state[1]-expected).abs()<=1e-8+1e-8*expected.abs());
        }
    }

    #[test]
    fn qr_refinement_never_worsens_componentwise_error(
        off in prop::collection::vec(-1_f64..1.,9),
        scales in prop::collection::vec(-6_i32..6,3),
        expected in prop::collection::vec(-2_f64..2.,3)
    ) {
        // Strict row diagonal dominance ensures a unique system. Independent
        // row scales challenge mixed units without choosing singular samples.
        let mut coefficients=off;
        for row in 0..3 {
            coefficients[row*3+row]+=4.;
            for column in 0..3 {coefficients[row*3+column]*=10_f64.powi(scales[row]);}
        }
        let matrix=Matrix {n:3,values:coefficients.clone()};
        let rhs:Vec<_>=(0..3).map(|row| (0..3).map(|col| coefficients[row*3+col]*expected[col]).sum()).collect();
        let raw=nalgebra::DMatrix::from_row_slice(3,3,&coefficients).qr().solve(&nalgebra::DVector::from_column_slice(&rhs)).unwrap();
        // Measure independently without applying acceptance: even a unique,
        // scaled system can have an unrefined vector outside the 1e-10 policy.
        // Exact-zero rows can normalize tiny rounding residuals to one.
        let error=|values:&[f64]| coefficients.chunks_exact(3).zip(&rhs).map(|(row,b)| {
            let products:Vec<_>=row.iter().zip(values).map(|(a,x)| a*x).collect();
            (products.iter().sum::<f64>()-b).abs()
                /(b.abs()+products.iter().map(|v| v.abs()).sum::<f64>()).max(f64::MIN_POSITIVE)
        }).fold(0_f64,f64::max);
        let before=error(raw.as_slice());
        let improved=DenseQr.solve(&matrix,&rhs).unwrap();
        let after=error(&improved);
        prop_assert!(after<=before*(1.+1e-14)+f64::MIN_POSITIVE);
    }
    #[test]
    fn generated_composite_be_matches_signed_two_terminal_equations(
        c_exp in -12_i32..-3, is_exp in -15_i32..-5,
        ratio in -6_f64..6., prior_ratio in -6_f64..6.,
        vt in 0.015_f64..0.06, h in 0.001_f64..1., common in -2_f64..2.
    ) {
        let c=10_f64.powi(c_exp); let isat=10_f64.powi(is_exp);
        let voltage=vt*ratio; let prior=vt*prior_ratio;
        let current=isat*ratio.exp_m1()+c*(voltage-prior)/h;
        let request:Request=serde_json::from_value(serde_json::json!({
            "schema_version":"1","case_id":"generated-composite",
            "circuit":{"nodes":["0","p","n"],"elements":[
                {"kind":"voltage","id":"VP","p":1,"n":0,"volts":common+voltage},
                {"kind":"voltage","id":"VN","p":2,"n":0,"volts":common},
                {"kind":"diode_with_capacitance","id":"D1","p":1,"n":2,"saturation_amps":isat,"thermal_volts":vt,"farads":c}
            ]},"analysis":{"kind":"operating_point"}
        })).unwrap();
        let plan=compile(&request.circuit).unwrap();
        let state=[common+voltage,common,-current,current];
        let previous=[common+prior,common,0.,0.];
        let system=circuit_simulator::newton::linearize_backward_euler(&plan,&state,&previous,h,plan.rhs()).unwrap();
        let scale=isat*ratio.exp()+c/h;
        prop_assert!(system.residual.iter().all(|r| r.abs()<1e-12*(1.+scale)));
        let conductance=isat*ratio.exp()/vt+c/h;
        prop_assert!((system.jacobian.values[0]-conductance).abs()<1e-11*(1.+conductance));
        prop_assert!((system.jacobian.values[1]+conductance).abs()<1e-11*(1.+conductance));
    }
    #[test]
    fn generated_constant_charge_obeys_scaling_symmetry_and_differential_law(
        capacitance_log in -18.0_f64..-3., voltage in -1000.0_f64..1000., scale_log in -3.0_f64..3.
    ) {
        use circuit_simulator::charge::constant_capacitance;
        let c=10_f64.powf(capacitance_log);
        let factor=10_f64.powf(scale_log);
        let original=constant_capacitance(c,voltage).unwrap();
        let reversed=constant_capacitance(c,-voltage).unwrap();
        prop_assert_eq!(reversed.charge_coulombs,-original.charge_coulombs);
        prop_assert_eq!(reversed.energy_joules,original.energy_joules);
        let transformed=constant_capacitance(c*factor,voltage/factor).unwrap();
        prop_assert!((transformed.charge_coulombs-original.charge_coulombs).abs() <= 16.*f64::EPSILON*original.charge_coulombs.abs().max(f64::MIN_POSITIVE));
        prop_assert!((transformed.energy_joules*factor-original.energy_joules).abs() <= 32.*f64::EPSILON*original.energy_joules.max(f64::MIN_POSITIVE));
        let h=(voltage.abs()*1e-6).max(1e-9);
        let derivative=(constant_capacitance(c,voltage+h).unwrap().charge_coulombs-
            constant_capacitance(c,voltage-h).unwrap().charge_coulombs)/(2.*h);
        prop_assert!((derivative-original.capacitance_farads).abs() < 1e-8*c);
    }

    #[test]
    fn arbitrary_charge_ieee_inputs_preserve_total_finite_domain_diagnostics(c in any::<u64>(),v in any::<u64>()) {
        use circuit_simulator::charge::constant_capacitance;
        let c=f64::from_bits(c); let v=f64::from_bits(v);
        let result=constant_capacitance(c,v);
        if !c.is_finite() || c<=0. || !v.is_finite() { prop_assert!(result.is_err()); }
        if let Ok(value)=result {
            prop_assert!(value.charge_coulombs.is_finite());
            prop_assert!(value.energy_joules.is_finite()&&value.energy_joules>=0.);
            prop_assert_eq!(value.capacitance_farads,c);
            if value.charge_coulombs!=0. { prop_assert_eq!(value.charge_coulombs.signum(),v.signum()); }
        }
    }
    #[test]
    fn generated_selected_current_sources_preserve_kcl_and_nominal_order(
        currents in prop::array::uniform3(-1000_i16..1000), replacement in -1000_i16..1000,
        selected in 0_usize..3, exponent in -100_i32..100
    ) {
        use circuit_simulator::{sources::{BoundSources, SourceOverride}, waveform::Waveform};
        let scale = 10_f64.powi(exponent);
        let nominal = currents.map(|value| f64::from(value) * scale);
        let replacement = f64::from(replacement) * scale;
        let plan = compile(&parse(&format!("title\nR1 out 0 1\nI1 0 out {}\nI2 out 0 {}\nI3 0 out {}\n.end", nominal[0], nominal[1], nominal[2])).unwrap()).unwrap();
        let sources = BoundSources::new(&plan, &[SourceOverride { id: format!("I{}", selected + 1), waveform: Waveform::Constant { value: replacement } }]).unwrap();
        let mut chosen = nominal;
        chosen[selected] = replacement;
        let expected = chosen[0] - chosen[1] + chosen[2];
        let actual = sources.rhs(0.).unwrap()[0];
        prop_assert!((actual - expected).abs() <= 16. * f64::EPSILON * chosen.iter().map(|value| value.abs()).sum::<f64>().max(f64::MIN_POSITIVE));
        prop_assert!(sources.rhs(f64::NAN).is_err());
        prop_assert!(sources.rhs(-1.).is_err());
    }
    #[test]
    fn generated_sparse_dynamic_rc_matches_exact_discrete_recurrence(
        resistance_log in 0.0_f64..5., ratio_log in -2.0_f64..2.,
        step_exponent in -15_i32..-5, steps in 1_usize..33, trapezoidal in any::<bool>()
    ) {
        use circuit_simulator::configured::{self, Method, TransientConfig};
        let h = 2_f64.powi(step_exponent);
        let ratio = 10_f64.powf(ratio_log);
        let resistance = 10_f64.powf(resistance_log);
        let capacitance = h / (ratio * resistance);
        let plan = compile(&parse(&format!("title\nR1 out 0 {resistance}\nC1 out 0 {capacitance}\n.end")).unwrap()).unwrap();
        let method = if trapezoidal { Method::Trapezoidal } else { Method::BackwardEuler };
        let options = TransientConfig { stop_s: h * steps as f64, max_step_s: h, max_steps: steps, method, initial_state: Some(vec![1.]), ..Default::default() };
        let decay = if trapezoidal { (2. - ratio) / (2. + ratio) } else { 1. / (1. + ratio) };
        for provider in [&DenseLu as &dyn LinearSolver, &FaerSparseLu] {
            let result = configured::run(&plan, provider, &options, &false.into()).unwrap();
            prop_assert_eq!(result.samples.len(), steps + 1);
            for (index, sample) in result.samples.iter().enumerate() {
                prop_assert!((sample.values[0] - decay.powi(index as i32)).abs() < 1e-10);
            }
            prop_assert_eq!(result.report.prepared_factors, 1);
            prop_assert_eq!(result.report.prepared_reuses, steps - 1);
        }
    }

    #[test]
    fn arbitrary_dynamic_inputs_return_total_finite_diagnostics(
        h in any::<u64>(), old in prop::collection::vec(any::<u64>(), 0..5),
        prior in prop::collection::vec(any::<u64>(), 0..5),
        current in prop::collection::vec(any::<u64>(), 0..5)
    ) {
        use circuit_simulator::{configured::Method, linear_dynamic::assemble};
        let plan = compile(&parse("title\nR1 out 0 1\nC1 out 0 1\n.end").unwrap()).unwrap();
        let convert = |bits: Vec<u64>| bits.into_iter().map(f64::from_bits).collect::<Vec<_>>();
        let old = convert(old); let prior = convert(prior); let current = convert(current);
        if let Ok(system) = assemble(&plan, Method::Trapezoidal, &old, f64::from_bits(h), &prior, &current) {
            prop_assert_eq!(system.rhs.len(), 1);
            prop_assert!(system.rhs.iter().chain(system.matrix.values()).all(|value| value.is_finite()));
        }
    }
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
