use circuit_simulator::pspice_switch::SwitchModel;
#[test]
fn midpoint_endpoints_and_control_derivative() {
    let m = SwitchModel {
        ron: 100.,
        roff: 10000.,
        von: 0.2,
        voff: 0.,
    };
    let c = m.linearize(0.05, 0.1).unwrap();
    assert!((c.current - 0.00005).abs() < 1e-14);
    assert!((c.output_siemens - 0.001).abs() < 1e-14);
    let eps = 1e-7;
    let numerical = (m.linearize(0.05, 0.1 + eps).unwrap().current
        - m.linearize(0.05, 0.1 - eps).unwrap().current)
        / (2. * eps);
    assert!((c.control_siemens - numerical).abs() < 1e-10);
    assert_eq!(m.linearize(1., -1.).unwrap().control_siemens, 0.);
    assert_eq!(m.linearize(1., 1.).unwrap().control_siemens, 0.);
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
    newton::{conductance_at_bias_csc, linearize},
};
fn divider(control: f64, reverse: bool) -> Circuit {
    Circuit {
        nodes: vec!["0".into(), "in".into(), "out".into(), "ctrl".into()],
        elements: vec![
            Element::Voltage {
                id: "drive".into(),
                p: N(1),
                n: N(0),
                volts: 0.1,
            },
            Element::Voltage {
                id: "ctrl".into(),
                p: N(3),
                n: N(0),
                volts: control,
            },
            Element::PspiceSwitch {
                id: "s".into(),
                p: N(1),
                n: N(2),
                cp: N(3),
                cn: N(0),
                ron: 100.,
                roff: 10000.,
                von: if reverse { 0. } else { 0.2 },
                voff: if reverse { 0.2 } else { 0. },
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
fn bug883_ac_output_and_control_gains_match_analytic_on_both_providers() {
    for solver in [
        &DenseLu as &dyn circuit_simulator::linalg::LinearSolver,
        &FaerSparseLu,
    ] {
        for reverse in [false, true] {
            let plan = compile(&divider(0.1, reverse)).unwrap();
            let bias = operating_point(&plan, solver).unwrap();
            assert!((bias[1] - 0.05).abs() < 1e-10);
            let input = ac_with_source_phasors(
                &plan,
                &[0.99, 1., 1.01],
                &[
                    AcSourcePhasor {
                        id: "drive".into(),
                        real: 1.,
                        imag: 0.,
                    },
                    AcSourcePhasor {
                        id: "ctrl".into(),
                        real: 0.,
                        imag: 0.,
                    },
                ],
                solver,
            )
            .unwrap();
            for point in input {
                assert!((point.values[1].real - 0.5).abs() < 1e-10);
                assert!(point.values[1].imag.abs() < 1e-12);
            }
            let ctrl = ac_with_source_phasors(
                &plan,
                &[1.],
                &[
                    AcSourcePhasor {
                        id: "drive".into(),
                        real: 0.,
                        imag: 0.,
                    },
                    AcSourcePhasor {
                        id: "ctrl".into(),
                        real: 1.,
                        imag: 0.,
                    },
                ],
                solver,
            )
            .unwrap();
            let expected = 0.1 * 1.5 * 100_f64.ln() / 0.2 / 4. * if reverse { -1. } else { 1. };
            assert!((ctrl[0].values[1].real - expected).abs() < 1e-9);
        }
    }
}
#[test]
fn full_mna_jacobian_matches_finite_differences_and_sparse_ac_stamps() {
    let plan = compile(&divider(0.08, false)).unwrap();
    let x = vec![0.15, 0.04, 0.08, -0.001, 0.];
    let system = linearize(&plan, &x).unwrap();
    let sparse = conductance_at_bias_csc(&plan, &x).unwrap();
    let mut dense = vec![0.; x.len() * x.len()];
    for ((row, col), value) in sparse.pattern().slots().zip(sparse.values()) {
        dense[row * x.len() + col] = *value;
    }
    for col in 0..x.len() {
        let mut plus = x.clone();
        let mut minus = x.clone();
        plus[col] += 1e-7;
        minus[col] -= 1e-7;
        let fp = linearize(&plan, &plus).unwrap();
        let fm = linearize(&plan, &minus).unwrap();
        for row in 0..x.len() {
            let fd = (fp.residual[row] - fm.residual[row]) / 2e-7;
            assert!((system.jacobian.values[row * system.jacobian.n + col] - fd).abs() < 1e-8);
            assert_eq!(
                dense[row * x.len() + col],
                system.jacobian.values[row * system.jacobian.n + col]
            );
        }
    }
}
#[test]
fn invalid_parameters_and_nonfinite_inputs_are_explicit_errors() {
    for m in [
        SwitchModel {
            ron: 0.,
            roff: 1.,
            von: 1.,
            voff: 0.,
        },
        SwitchModel {
            ron: 100.,
            roff: 10.,
            von: 1.,
            voff: 0.,
        },
        SwitchModel {
            ron: 1.,
            roff: 1e13,
            von: 1.,
            voff: 0.,
        },
        SwitchModel {
            ron: 1.,
            roff: 1e3,
            von: 0.,
            voff: 0.,
        },
        SwitchModel {
            ron: 1.,
            roff: 1e3,
            von: f64::INFINITY,
            voff: 0.,
        },
    ] {
        assert!(m.validate().is_err());
    }
    let m = SwitchModel {
        ron: 100.,
        roff: 10000.,
        von: 0.2,
        voff: 0.,
    };
    assert!(m.linearize(f64::NAN, 0.).is_err());
    assert!(m.linearize(1., f64::INFINITY).is_err());
    let low=SwitchModel{ron:1e-3,roff:1e12,von:0.2,voff:0.};
    assert!(low.linearize(f64::MAX,0.2).is_err());
    let mut c = divider(0.1, false);
    if let Element::PspiceSwitch { cp, .. } = &mut c.elements[2] {
        *cp = N(99);
    }
    assert!(compile(&c).is_err());
}
use proptest::prelude::*;
proptest! {
 #![proptest_config(ProptestConfig::with_cases(128))]
 #[test]
 fn passive_smooth_model_is_bounded_and_derivative_matches_numerics(t in 0.02f64..0.98,v in -2f64..2.,ron in 1f64..1000.,ratio in 1.1f64..1000.){
  let m=SwitchModel{ron,roff:ron*ratio,von:1.,voff:0.};let c=m.linearize(v,t).unwrap();
  prop_assert!(c.output_siemens>=1./m.roff*(1.-1e-12)&&c.output_siemens<=1./m.ron*(1.+1e-12));
  prop_assert!(v*c.current>=0.);
  let fd=(m.linearize(v,t+1e-6).unwrap().current-m.linearize(v,t-1e-6).unwrap().current)/2e-6;
  prop_assert!((fd-c.control_siemens).abs()<1e-7*(1.+fd.abs()));
 }
}

#[test]
fn configured_initial_state_checks_switch_current_before_observer() {
    use circuit_simulator::configured::{TransientConfig, run_observed};
    let plan = compile(&divider(0.1, false)).unwrap();
    let cancel = std::sync::atomic::AtomicBool::new(false);
    let mut options = TransientConfig {
        stop_s: 1e-3,
        max_step_s: 1e-3,
        initial_state: Some(vec![0.1, 0., 0.1, 0., 0.]),
        ..Default::default()
    };
    let mut observed = 0;
    assert!(
        run_observed(&plan, &DenseLu, &options, &cancel, |_| {
            observed += 1;
            Ok(())
        })
        .is_err()
    );
    assert_eq!(observed, 0);
    options.initial_state = Some(vec![0.1, 0.05, 0.1, -0.00005, 0.]);
    let result = run_observed(&plan, &DenseLu, &options, &cancel, |_| {
        observed += 1;
        Ok(())
    })
    .unwrap();
    assert_eq!(observed, 2);
    assert!((result.samples[1].values[1] - 0.05).abs() < 1e-10);
}
