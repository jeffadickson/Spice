//! Independent constitutive and differential checks for R(Vcontrol).
use circuit_simulator::{behavioral_resistor::linearize, expression::Expr};

fn law() -> Expr {
    Expr::Add(Box::new(Expr::Constant(1000.)), Box::new(Expr::Input))
}

#[test]
fn signed_current_and_both_partials_follow_the_quotient_rule() {
    let c = linearize(&law(), 100., 100.).unwrap();
    assert!((c.current - 100. / 1100.).abs() < 1e-15);
    assert!((c.output_siemens - 1. / 1100.).abs() < 1e-15);
    assert!((c.control_siemens + 100. / (1100. * 1100.)).abs() < 1e-15);
    let reversed = linearize(&law(), -100., 100.).unwrap();
    assert_eq!(reversed.current, -c.current);
    assert_eq!(reversed.control_siemens, -c.control_siemens);
    assert_eq!(reversed.output_siemens, c.output_siemens);
}

#[test]
fn singular_and_nonfinite_trials_are_errors_without_clamps() {
    for (out, control) in [(1., -1000.), (f64::NAN, 0.), (1., f64::INFINITY)] {
        assert!(linearize(&law(), out, control).is_err());
    }
    assert!(linearize(&Expr::Constant(1e-300), f64::MAX, 0.).is_err());
    // Negative resistance is an explicit active constitutive law, not clamped.
    assert_eq!(
        linearize(&Expr::Constant(-2.), 4., 0.).unwrap().current,
        -2.
    );
}

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
};

fn divider() -> Circuit {
    Circuit {
        nodes: vec!["0".into(), "in".into(), "out".into(), "ctrl".into()],
        elements: vec![
            Element::Voltage {
                id: "drive".into(),
                p: N(1),
                n: N(0),
                volts: 100.,
            },
            Element::Voltage {
                id: "control".into(),
                p: N(3),
                n: N(0),
                volts: 100.,
            },
            Element::BehavioralResistor {
                id: "behavior".into(),
                p: N(1),
                n: N(2),
                cp: N(3),
                cn: N(0),
                expression: law(),
            },
            Element::Resistor {
                id: "load".into(),
                p: N(2),
                n: N(0),
                ohms: 1000.,
            },
        ],
    }
}

#[test]
fn both_providers_use_actual_bias_and_signed_control_gain_in_ac() {
    let plan = compile(&divider()).unwrap();
    for solver in [
        &DenseLu as &dyn circuit_simulator::linalg::LinearSolver,
        &FaerSparseLu,
    ] {
        let dc = operating_point(&plan, solver).unwrap();
        assert!((dc[1] - 100. * 1000. / 2100.).abs() < 1e-10);
        for (input, control, expected) in [
            (1., 0., 1000. / 2100.),
            (0., 1., -100. * 1000. / (2100. * 2100.)),
        ] {
            let points = ac_with_source_phasors(
                &plan,
                &[1., 1000.],
                &[
                    AcSourcePhasor {
                        id: "drive".into(),
                        real: input,
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
            for point in points {
                assert!((point.values[1].real - expected).abs() < 1e-10);
                assert!(point.values[1].imag.abs() < 1e-12);
            }
        }
    }
}

#[test]
fn complete_jacobian_matches_finite_differences_and_sparse_incremental_stamps() {
    let plan = compile(&divider()).unwrap();
    let state = vec![110., 43., 120., -0.03, 0.];
    let exact = mna(&plan, &state).unwrap();
    let sparse = conductance_at_bias_csc(&plan, &state).unwrap();
    let mut dense = vec![0.; state.len() * state.len()];
    for ((row, col), value) in sparse.pattern().slots().zip(sparse.values()) {
        dense[row * state.len() + col] = *value;
    }
    for col in 0..state.len() {
        let mut plus = state.clone();
        let mut minus = state.clone();
        plus[col] += 1e-5;
        minus[col] -= 1e-5;
        let p = mna(&plan, &plus).unwrap();
        let m = mna(&plan, &minus).unwrap();
        for row in 0..state.len() {
            let slot = row * state.len() + col;
            assert!(
                (exact.jacobian.values[slot] - (p.residual[row] - m.residual[row]) / 2e-5).abs()
                    < 1e-8
            );
            assert_eq!(exact.jacobian.values[slot], dense[slot]);
        }
    }
}

#[test]
fn output_control_aliasing_adds_both_partials_instead_of_overwriting() {
    let mut circuit = divider();
    if let Element::BehavioralResistor { cp, cn, .. } = &mut circuit.elements[2] {
        *cp = N(1);
        *cn = N(2);
    }
    let plan = compile(&circuit).unwrap();
    let x = vec![100., 40., 100., 0., 0.];
    let system = mna(&plan, &x).unwrap();
    let expected = 1000. / (1060. * 1060.);
    assert!((system.jacobian.values[0] - expected).abs() < 1e-15);
    assert!((system.jacobian.values[1] + expected).abs() < 1e-15);
}

#[test]
fn wire_validation_and_independent_audit_preserve_failures() {
    let circuit = divider();
    let mut wire = serde_json::to_value(&circuit).unwrap();
    wire["elements"][2]["invented"] = serde_json::json!(1);
    assert!(serde_json::from_value::<Circuit>(wire).is_err());
    let mut wire = serde_json::to_value(&circuit).unwrap();
    wire["elements"][2]["expression"]["op"] = serde_json::json!("shell");
    assert!(serde_json::from_value::<Circuit>(wire).is_err());
    let mut bad = circuit.clone();
    if let Element::BehavioralResistor { cp, .. } = &mut bad.elements[2] {
        *cp = N(99);
    }
    assert!(compile(&bad).is_err());
    let plan = compile(&circuit).unwrap();
    let mut state = operating_point(&plan, &DenseLu).unwrap();
    let audit = circuit_simulator::audit::dc(&circuit, &state, Default::default(), None).unwrap();
    assert!(!audit.has_failures());
    let power = circuit_simulator::power::dc_power(&plan, &state).unwrap();
    assert!(power.sum_absorbed_w.abs() < 1e-10);
    state[1] += 0.1;
    assert!(
        circuit_simulator::audit::dc(&circuit, &state, Default::default(), None)
            .unwrap()
            .has_failures()
    );
}

#[test]
fn public_wire_entry_executes_dc_and_ac_and_marks_bias_audit_unavailable() {
    use circuit_simulator::api::{Request, run};
    for backend in ["lu", "faer"] {
        let mut wire = serde_json::json!({"schema_version":"1","case_id":"resistance_wire","backend":backend,
            "circuit":divider(),"analysis":{"kind":"operating_point"}});
        let request: Request = serde_json::from_value(wire.clone()).unwrap();
        let result = run(&request).unwrap();
        assert!((result.samples[0].values[1] - 100. * 1000. / 2100.).abs() < 1e-10);
        assert!(!result.consistency.has_failures());
        wire["analysis"] = serde_json::json!({"kind":"ac","frequencies_hz":[1000.],"source_phasors":[
            {"id":"drive","real":0.,"imag":0.},{"id":"control","real":1.,"imag":0.}
        ]});
        let result = run(&serde_json::from_value(wire).unwrap()).unwrap();
        assert!(
            (result.ac_samples[0].values[1].real + 100. * 1000. / (2100. * 2100.)).abs() < 1e-10
        );
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
    #![proptest_config(proptest::test_runner::Config {
        cases:128, rng_seed:proptest::test_runner::RngSeed::Fixed(202611131001), ..Default::default()
    })]
    #[test]
    fn finite_resistance_derivatives_match_independent_centered_differences(
        output in -200f64..200., control in -100f64..100., base in 500f64..2000., slope in -2f64..2.
    ) {
        let expression=Expr::Add(Box::new(Expr::Constant(base)),Box::new(Expr::Multiply(Box::new(Expr::Constant(slope)),Box::new(Expr::Input))));
        let c=linearize(&expression,output,control).unwrap();
        let current=|out:f64,ctrl:f64| out/(base+slope*ctrl);
        let derivative=(current(output,control+1e-3)-current(output,control-1e-3))/2e-3;
        proptest::prop_assert!((c.control_siemens-derivative).abs()<1e-10);
        proptest::prop_assert!((c.current-current(output,control)).abs()<1e-14);
        proptest::prop_assert!(output*c.current>=0.);
    }
}
