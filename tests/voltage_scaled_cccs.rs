//! Exact product-law checks; this does not qualify an entire Gummel–Poon BJT.
use circuit_simulator::{
    ac::ac_with_source_phasors,
    api::AcSourcePhasor,
    backend::DenseLu,
    backend_faer::FaerSparseLu,
    circuit::{Circuit, Element},
    compile::compile,
    dc::operating_point,
    ids::NodeId as N,
    newton::{conductance_at_bias_csc, linearize as mna},
    voltage_scaled_cccs::linearize,
};

fn circuit() -> Circuit {
    Circuit {
        nodes: vec!["0".into(), "sense".into(), "ctrl".into(), "out".into()],
        elements: vec![
            Element::Voltage {
                id: "sense".into(),
                p: N(1),
                n: N(0),
                volts: 1.,
            },
            Element::Resistor {
                id: "sense_load".into(),
                p: N(1),
                n: N(0),
                ohms: 100.,
            },
            Element::Voltage {
                id: "control".into(),
                p: N(2),
                n: N(0),
                volts: 2.,
            },
            Element::VoltageScaledCccs {
                id: "product".into(),
                p: N(3),
                n: N(0),
                cp: N(2),
                cn: N(0),
                control_id: "sense".into(),
                gain: 2.,
                voltage_coefficient: 0.25,
                factor_offset: 1.,
            },
            Element::Resistor {
                id: "load".into(),
                p: N(3),
                n: N(0),
                ohms: 1000.,
            },
        ],
    }
}

#[test]
fn product_current_and_partials_have_signed_units() {
    let c = linearize(2., 0.25, -0.01, 2.).unwrap();
    assert!((c.current_amps + 0.03).abs() < 1e-15);
    assert_eq!(c.current_gain, 3.);
    assert_eq!(c.control_siemens, -0.005);
    assert_eq!(
        linearize(2., 0.25, 0.01, 2.).unwrap().current_amps,
        -c.current_amps
    );
}

#[test]
fn actual_dc_bias_controls_both_incremental_gains_on_both_providers() {
    let plan = compile(&circuit()).unwrap();
    for solver in [
        &DenseLu as &dyn circuit_simulator::linalg::LinearSolver,
        &FaerSparseLu,
    ] {
        assert!((operating_point(&plan, solver).unwrap()[2] - 30.).abs() < 1e-11);
        for (sense, control, expected) in [(1., 0., 30.), (0., 1., 5.)] {
            let result = ac_with_source_phasors(
                &plan,
                &[1., 1000.],
                &[
                    AcSourcePhasor {
                        id: "sense".into(),
                        real: sense,
                        imag: 0.,
                    },
                    AcSourcePhasor {
                        id: "control".into(),
                        real: control,
                        imag: 0.,
                    },
                ],
                solver,
            )
            .unwrap();
            for point in result {
                assert!((point.values[2].real - expected).abs() < 1e-10);
                assert!(point.values[2].imag.abs() < 1e-12);
            }
        }
    }
}

#[test]
fn every_mna_partial_matches_finite_differences_and_sparse_ac_stamps() {
    let plan = compile(&circuit()).unwrap();
    let state = vec![1.1, 1.9, 24., -0.012, 0.];
    let exact = mna(&plan, &state).unwrap();
    let sparse = conductance_at_bias_csc(&plan, &state).unwrap();
    let mut dense = vec![0.; state.len() * state.len()];
    for ((r, c), v) in sparse.pattern().slots().zip(sparse.values()) {
        dense[r * state.len() + c] = *v;
    }
    for column in 0..state.len() {
        let mut plus = state.clone();
        let mut minus = state.clone();
        plus[column] += 1e-6;
        minus[column] -= 1e-6;
        let p = mna(&plan, &plus).unwrap();
        let m = mna(&plan, &minus).unwrap();
        for row in 0..state.len() {
            let slot = row * state.len() + column;
            assert!(
                (exact.jacobian.values[slot] - (p.residual[row] - m.residual[row]) / 2e-6).abs()
                    < 1e-8
            );
            assert_eq!(exact.jacobian.values[slot], dense[slot]);
        }
    }
}

#[test]
fn control_output_aliases_accumulate_and_do_not_create_a_second_branch() {
    let mut c = circuit();
    if let Element::VoltageScaledCccs {
        cp,
        voltage_coefficient,
        ..
    } = &mut c.elements[3]
    {
        *cp = N(3);
        *voltage_coefficient = 0.001;
    }
    let plan = compile(&c).unwrap();
    assert_eq!(plan.unknown_count(), 5);
    let state = operating_point(&plan, &DenseLu).unwrap();
    assert!((state[2] - 20. / 0.98).abs() < 1e-10);
}

#[test]
fn wire_domains_and_independent_physical_audit_preserve_errors() {
    let c = circuit();
    let plan = compile(&c).unwrap();
    let mut state = operating_point(&plan, &DenseLu).unwrap();
    assert!(
        !circuit_simulator::audit::dc(&c, &state, Default::default(), None)
            .unwrap()
            .has_failures()
    );
    assert!(
        circuit_simulator::power::dc_power(&plan, &state)
            .unwrap()
            .sum_absorbed_w
            .abs()
            < 1e-12
    );
    state[2] += 0.1;
    assert!(
        circuit_simulator::audit::dc(&c, &state, Default::default(), None)
            .unwrap()
            .has_failures()
    );
    let mut bad = c.clone();
    if let Element::VoltageScaledCccs { cp, .. } = &mut bad.elements[3] {
        *cp = N(99);
    }
    assert!(compile(&bad).is_err());
    for field in ["control_id", "cp", "gain", "invented"] {
        let mut wire = serde_json::to_value(&c).unwrap();
        wire["elements"][3][field] = serde_json::json!(if field == "control_id" {
            "missing"
        } else {
            "invalid"
        });
        let parsed = serde_json::from_value::<Circuit>(wire);
        assert!(parsed.is_err() || compile(&parsed.unwrap()).is_err());
    }
    for args in [
        (f64::NAN, 1., 1., 1.),
        (1., f64::INFINITY, 1., 1.),
        (1., 1., f64::NAN, 1.),
        (1., 1., 1., f64::INFINITY),
        (f64::MAX, 1., f64::MAX, 1.),
    ] {
        assert!(linearize(args.0, args.1, args.2, args.3).is_err());
    }
}

#[test]
fn public_api_preserves_dc_results_and_marks_unavailable_nonlinear_ac_audit() {
    use circuit_simulator::api::{Request, run};
    for backend in ["lu", "faer"] {
        let mut wire = serde_json::json!({"schema_version":"1","case_id":"scaled_current_wire","backend":backend,
            "circuit":circuit(),"analysis":{"kind":"operating_point"}});
        let request: Request = serde_json::from_value(wire.clone()).unwrap();
        let result = run(&request).unwrap();
        assert!((result.samples[0].values[2] - 30.).abs() < 1e-10);
        assert!(!result.consistency.has_failures());
        wire["analysis"] = serde_json::json!({"kind":"ac","frequencies_hz":[1000.],"source_phasors":[
            {"id":"sense","real":0.,"imag":0.},{"id":"control","real":1.,"imag":0.}
        ]});
        let result = run(&serde_json::from_value(wire).unwrap()).unwrap();
        assert!((result.ac_samples[0].values[2].real - 5.).abs() < 1e-10);
        assert!(
            result
                .consistency
                .checks
                .iter()
                .any(|c| c.id == "independent_physical_audit"
                    && c.status == circuit_simulator::audit::CheckStatus::NotChecked)
        );
    }
}

proptest::proptest! {
    #![proptest_config(proptest::test_runner::Config {cases:128,rng_seed:proptest::test_runner::RngSeed::Fixed(202611131102),..Default::default()})]
    #[test]
    fn typed_boundary_fuzz_refuses_invalid_nodes_and_nonfinite_coefficients(
        cp in 0usize..8,cn in 0usize..8,gain in proptest::num::f64::ANY,coefficient in proptest::num::f64::ANY
    ){
        let mut c=circuit();
        if let Element::VoltageScaledCccs{cp:p,cn:n,gain:g,voltage_coefficient:k,..}=&mut c.elements[3]{*p=N(cp);*n=N(cn);*g=gain;*k=coefficient;}
        let structurally_valid=cp<4&&cn<4&&gain.is_finite()&&coefficient.is_finite();
        proptest::prop_assert_eq!(compile(&c).is_ok(),structurally_valid);
    }
}

proptest::proptest! {
    #![proptest_config(proptest::test_runner::Config {cases:128,rng_seed:proptest::test_runner::RngSeed::Fixed(202611131101),..Default::default()})]
    #[test]
    fn both_independent_partial_derivatives_match_centered_differences(
        gain in -100f64..100.,coefficient in -2f64..2.,current in -0.1f64..0.1,volts in -10f64..10.
    ){
        let c=linearize(gain,coefficient,current,volts).unwrap();
        let independent=|i:f64,v:f64|gain*i+gain*coefficient*i*v;
        let di=(independent(current+1e-6,volts)-independent(current-1e-6,volts))/2e-6;
        let dv=(independent(current,volts+1e-5)-independent(current,volts-1e-5))/2e-5;
        proptest::prop_assert!((c.current_gain-di).abs()<1e-7);
        proptest::prop_assert!((c.control_siemens-dv).abs()<1e-8);
        proptest::prop_assert!((c.current_amps-independent(current,volts)).abs()<1e-11);
    }
}

#[test]
fn explicit_zero_bias_product_preserves_legacy_defaults_and_both_ac_partials() {
    let legacy=serde_json::to_value(circuit()).unwrap();
    assert!(legacy["elements"][3].get("factor_offset").is_none());
    let mut wire=legacy;
    wire["elements"][3]["factor_offset"]=serde_json::json!(0.);
    let product: Circuit=serde_json::from_value(wire).expect("explicit zero-bias product must be recognized");
    let plan=compile(&product).unwrap();
    for solver in [&DenseLu as &dyn circuit_simulator::linalg::LinearSolver,&FaerSparseLu] {
        assert!((operating_point(&plan,solver).unwrap()[2]-10.).abs()<1e-11);
        for (sense,control,expected) in [(1.,0.,10.),(0.,1.,5.)] {
            let result=ac_with_source_phasors(&plan,&[1.,1000.],&[
                AcSourcePhasor{id:"sense".into(),real:sense,imag:0.},
                AcSourcePhasor{id:"control".into(),real:control,imag:0.}],solver).unwrap();
            for point in result { assert!((point.values[2].real-expected).abs()<1e-10); }
        }
    }
}

#[test]
fn zero_bias_avoids_unit_factor_cancellation_and_rejects_nonfinite_offsets() {
    use circuit_simulator::voltage_scaled_cccs::linearize_with_offset;
    for voltage in [1e-200,1e-20,-1e-20,1e20] {
        let c=linearize_with_offset(1.,1.,0.,1.,voltage).unwrap();
        assert_ne!(c.current_amps,0.);
        assert!((c.current_amps/voltage-1.).abs()<1e-15);
        assert_eq!(c.current_gain,voltage);
        assert_eq!(c.control_siemens,1.);
    }
    assert!(linearize_with_offset(1.,1.,f64::NAN,1.,1.).is_err());
    let mut invalid=circuit();
    if let Element::VoltageScaledCccs{factor_offset,..}=&mut invalid.elements[3]{*factor_offset=f64::INFINITY;}
    assert!(compile(&invalid).is_err());
}

proptest::proptest! {
    #![proptest_config(proptest::test_runner::Config {cases:128,rng_seed:proptest::test_runner::RngSeed::Fixed(2026111511),..Default::default()})]
    #[test]
    fn optional_offset_partials_match_independent_centered_differences(
        gain in -10f64..10.,coefficient in -2f64..2.,offset in -2f64..2.,current in -0.1f64..0.1,volts in -10f64..10.
    ) {
        let c=circuit_simulator::voltage_scaled_cccs::linearize_with_offset(gain,coefficient,offset,current,volts).unwrap();
        let independent=|i:f64,v:f64|gain*offset*i+gain*coefficient*i*v;
        let di=(independent(current+1e-6,volts)-independent(current-1e-6,volts))/2e-6;
        let dv=(independent(current,volts+1e-5)-independent(current,volts-1e-5))/2e-5;
        proptest::prop_assert!((c.current_gain-di).abs()<1e-8);
        proptest::prop_assert!((c.control_siemens-dv).abs()<1e-9);
        proptest::prop_assert!((c.current_amps-independent(current,volts)).abs()<1e-12);
    }
}
