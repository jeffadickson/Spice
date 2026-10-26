//! Coupled nonlinear circuit oracles independent of production residual assembly.
use circuit_simulator::{
    backend::{DenseLu, DenseQr},
    backend_faer::FaerSparseLu,
    circuit::{Circuit, Element},
    compile::compile,
    ids::NodeId,
    linalg::LinearSolver,
    newton::{ContinuationOptions, GminContinuationOptions, NewtonOptions, solve_from},
};

fn policies() -> [NewtonOptions; 2] {
    let common = NewtonOptions {
        max_iterations: 128,
        current_absolute_tolerance: 1e-16,
        ..NewtonOptions::default()
    };
    [
        NewtonOptions {
            continuation: Some(ContinuationOptions {
                initial_step: 0.125,
                minimum_step: 1e-6,
                max_stages: 128,
            }),
            ..common
        },
        NewtonOptions {
            gmin_continuation: Some(GminContinuationOptions {
                initial_siemens: 1e-3,
                minimum_siemens: 1e-10,
                reduction_factor: 0.1,
                max_stages: 16,
            }),
            ..common
        },
    ]
}

fn chain(parameters: &[(f64, f64)], amps: f64) -> Circuit {
    let n = parameters.len();
    let mut elements = vec![Element::Current {
        id: "drive".into(),
        p: NodeId(0),
        n: NodeId(n),
        amps,
    }];
    for (index, &(saturation_amps, thermal_volts)) in parameters.iter().enumerate() {
        elements.push(Element::Diode {
            id: format!("D{}", index + 1),
            p: NodeId(index + 1),
            n: NodeId(index),
            saturation_amps,
            thermal_volts,
        });
    }
    Circuit {
        nodes: (0..=n).map(|index| index.to_string()).collect(),
        elements,
    }
}

#[test]
fn antiparallel_diodes_match_signed_asinh_oracle() {
    // Opposed identical diodes give I=2*Is*sinh(V/Vt), including reverse drive.
    // This is a solvable reverse-bias topology, unlike the lone-diode case.
    for amps in [-1e-3_f64, -1e-6, -1e-10, 1e-10, 1e-6, 1e-3] {
        let isat = 1e-12;
        let vt = 0.02585;
        let plan = compile(&Circuit {
            nodes: vec!["0".into(), "x".into()],
            elements: vec![
                Element::Current {
                    id: "drive".into(),
                    p: NodeId(0),
                    n: NodeId(1),
                    amps,
                },
                Element::Diode {
                    id: "forward".into(),
                    p: NodeId(1),
                    n: NodeId(0),
                    saturation_amps: isat,
                    thermal_volts: vt,
                },
                Element::Diode {
                    id: "reverse".into(),
                    p: NodeId(0),
                    n: NodeId(1),
                    saturation_amps: isat,
                    thermal_volts: vt,
                },
            ],
        })
        .unwrap();
        let expected = vt * (amps / (2. * isat)).asinh();
        for provider in [&DenseLu as &dyn LinearSolver, &DenseQr, &FaerSparseLu] {
            for options in policies() {
                let (values, report) =
                    solve_from(&plan, provider, options, vec![-amps.signum() * 0.1]).unwrap();
                assert!(
                    (values[0] - expected).abs() < 2e-7,
                    "{}: {amps}",
                    provider.name()
                );
                let current = isat * ((values[0] / vt).exp_m1() - (-values[0] / vt).exp_m1());
                assert!((current - amps).abs() < 1e-12);
                assert!(values[0] * current > 0.);
                assert_eq!(report.gmin_siemens, None);
            }
        }
    }
}

#[test]
fn parallel_diode_permutations_preserve_aggregate_shockley_solution() {
    let saturations = [1e-14, 3e-12, 7e-11];
    let total: f64 = saturations.iter().sum();
    for amps in [1e-9, 1e-6, 1e-3] {
        let expected = 0.03 * (amps / total).ln_1p();
        for reverse_order in [false, true] {
            let mut elements: Vec<_> = saturations
                .iter()
                .enumerate()
                .map(|(index, &saturation_amps)| Element::Diode {
                    id: format!("D{index}"),
                    p: NodeId(1),
                    n: NodeId(0),
                    saturation_amps,
                    thermal_volts: 0.03,
                })
                .collect();
            if reverse_order {
                elements.reverse();
            }
            elements.push(Element::Current {
                id: "I".into(),
                p: NodeId(0),
                n: NodeId(1),
                amps,
            });
            let plan = compile(&Circuit {
                nodes: vec!["0".into(), "x".into()],
                elements,
            })
            .unwrap();
            for provider in [&DenseLu as &dyn LinearSolver, &DenseQr, &FaerSparseLu] {
                for options in policies() {
                    let (values, _) = solve_from(&plan, provider, options, vec![-0.1]).unwrap();
                    assert!((values[0] - expected).abs() < 2e-7);
                    let currents: Vec<_> = saturations
                        .iter()
                        .map(|isat| isat * (values[0] / 0.03).exp_m1())
                        .collect();
                    assert!((currents.iter().sum::<f64>() - amps).abs() < 1e-12);
                    for i in 0..3 {
                        assert!((currents[i] / amps - saturations[i] / total).abs() < 1e-7);
                    }
                }
            }
        }
    }
}

fn assert_chain(values: &[f64], parameters: &[(f64, f64)], amps: f64) {
    let mut expected = 0.;
    let mut currents = Vec::new();
    for (index, &(isat, vt)) in parameters.iter().enumerate() {
        expected += vt * (amps / isat).ln_1p();
        assert!((values[index] - expected).abs() < 2e-7 * (index + 1) as f64);
        let previous = if index == 0 { 0. } else { values[index - 1] };
        currents.push(isat * ((values[index] - previous) / vt).exp_m1());
    }
    // Interior node KCL compares adjacent branch currents. Top-node KCL
    // compares the independently reconstructed diode current to excitation.
    for index in 0..currents.len() {
        let incoming = if index + 1 == currents.len() {
            amps
        } else {
            currents[index + 1]
        };
        assert!(
            (currents[index] - incoming).abs() < 4e-12,
            "KCL node {}: {:?}",
            index + 1,
            currents
        );
    }
}

#[test]
fn unequal_series_diodes_recover_across_both_policies_and_cpu_providers() {
    let providers: [&dyn LinearSolver; 3] = [&DenseLu, &DenseQr, &FaerSparseLu];
    for count in [2_usize, 4, 8] {
        let parameters: Vec<_> = (0..count)
            .map(|i| (10_f64.powi(-13 + (i % 3) as i32), 0.01 + 0.005 * i as f64))
            .collect();
        let plan = compile(&chain(&parameters, 1e-6)).unwrap();
        let mut initial = Vec::new();
        let mut sum = 0.;
        for &(_, vt) in &parameters {
            sum -= 4. * vt;
            initial.push(sum);
        }
        for provider in providers {
            for options in policies() {
                let (values, report) =
                    solve_from(&plan, provider, options, initial.clone()).unwrap();
                assert_chain(&values, &parameters, 1e-6);
                assert_eq!(report.gmin_siemens, None);
                if let Some(stages) = report.gmin_continuation {
                    assert!(stages.final_unregularized);
                }
            }
        }
    }
}

#[test]
fn seeded_coupled_chain_corpus_matches_inverse_shockley_and_kcl() {
    // Replay seed 0x434841494e333431; bounded mathematical parameter stress,
    // not physical temperature calibration or a uniqueness proof.
    let mut state = 0x4348_4149_4e33_3431_u64;
    let mut cases = 0;
    for index in 0..12 {
        let count = 2 + index % 3;
        let mut unit = || {
            state = state.wrapping_mul(6364136223846793005).wrapping_add(1);
            ((state >> 11) as f64) / ((1u64 << 53) as f64)
        };
        let amps = 10_f64.powf(-7. + 2. * unit());
        let parameters: Vec<_> = (0..count)
            .map(|_| (10_f64.powf(-13. + 2. * unit()), 0.01 + 0.03 * unit()))
            .collect();
        let plan = compile(&chain(&parameters, amps)).unwrap();
        for provider in [&DenseLu as &dyn LinearSolver, &DenseQr, &FaerSparseLu] {
            for options in policies() {
                let (values, _) = solve_from(&plan, provider, options, vec![0.; count]).unwrap();
                assert_chain(&values, &parameters, amps);
                cases += 1;
            }
        }
    }
    assert_eq!(cases, 72);
}

fn bridge(volts: f64, ohms: f64) -> Circuit {
    let mut elements = vec![
        Element::Voltage {
            id: "V".into(),
            p: NodeId(1),
            n: NodeId(0),
            volts,
        },
        Element::Resistor {
            id: "load".into(),
            p: NodeId(2),
            n: NodeId(3),
            ohms,
        },
    ];
    for (id, p, n) in [("D1", 1, 2), ("D2", 0, 2), ("D3", 3, 1), ("D4", 3, 0)] {
        elements.push(Element::Diode {
            id: id.into(),
            p: NodeId(p),
            n: NodeId(n),
            saturation_amps: 1e-12,
            thermal_volts: 0.02585,
        });
    }
    Circuit {
        nodes: vec!["0".into(), "in".into(), "plus".into(), "minus".into()],
        elements,
    }
}

fn bridge_voltage(volts: f64, ohms: f64) -> f64 {
    // Symmetry gives Vplus=(Vs+W)/2 and Vminus=(Vs-W)/2. Including both
    // reverse diode currents, W/R=2*Is*(exp(-W/(2*Vt))*cosh(Vs/(2*Vt))-1).
    // The scalar residual is strictly increasing on [0,abs(Vs)]; bisection
    // is independent of the production MNA Jacobian/Newton controller.
    let mut low = 0.;
    let mut high = volts.abs();
    for _ in 0..100 {
        let mid = (low + high) * 0.5;
        let current =
            2e-12 * ((-mid / (2. * 0.02585)).exp() * (volts / (2. * 0.02585)).cosh() - 1.);
        if mid / ohms > current {
            high = mid
        } else {
            low = mid
        }
    }
    (low + high) * 0.5
}

#[test]
fn bridge_rectifier_matches_monotonic_oracle_kcl_and_power_for_both_polarities() {
    for volts in [-2., -1., -0.5, 0.5, 1., 2.] {
        let ohms = 1000.;
        let plan = compile(&bridge(volts, ohms)).unwrap();
        let expected = bridge_voltage(volts, ohms);
        for provider in [&DenseLu as &dyn LinearSolver, &DenseQr, &FaerSparseLu] {
            for options in policies() {
                let options = NewtonOptions {
                    current_absolute_tolerance: 1e-12,
                    ..options
                };
                let (x, report) =
                    solve_from(&plan, provider, options, vec![0.; 4]).unwrap_or_else(|error| {
                        panic!(
                            "Vs={volts}, provider={}, options={options:?}: {error:?}",
                            provider.name()
                        )
                    });
                assert_eq!(report.gmin_siemens, None);
                let [input, plus, minus, branch] = x.as_slice() else {
                    panic!("unexpected MNA layout")
                };
                assert!((input - volts).abs() < 2e-8);
                assert!((plus - (volts + expected) * 0.5).abs() < 2e-7);
                assert!((minus - (volts - expected) * 0.5).abs() < 2e-7);
                let currents = [input - plus, -plus, minus - input, *minus]
                    .map(|v| 1e-12 * (v / 0.02585).exp_m1());
                let load = (plus - minus) / ohms;
                let kcl = [
                    branch + currents[0] - currents[2],
                    load - currents[0] - currents[1],
                    currents[2] + currents[3] - load,
                ];
                for mismatch in kcl {
                    assert!(mismatch.abs() < 4e-12, "Vs={volts}: KCL {kcl:?}");
                }
                let power = volts * branch + (plus - minus) * load + (input - plus) * currents[0]
                    - plus * currents[1]
                    + (minus - input) * currents[2]
                    + minus * currents[3];
                assert!(power.abs() < 2e-12, "Vs={volts}: power={power}");
            }
        }
    }
}
