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
    for count in [2, 4, 8] {
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
