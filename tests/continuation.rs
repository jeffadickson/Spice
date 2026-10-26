use circuit_simulator::{
    backend::{DenseLu, DenseQr},
    backend_faer::FaerSparseLu,
    circuit::{Circuit, Element},
    compile::compile,
    error::ErrorCode,
    ids::NodeId,
    linalg::LinearSolver,
    newton::{ContinuationOptions, GminContinuationOptions, NewtonOptions, solve},
};

// A one-node current-driven diode has the independent inverse-Shockley oracle
// V = Vt * ln(1 + I/Is). No resistor masks the poorly conditioned zero-bias start.
fn current_diode(amps: f64) -> Circuit {
    Circuit {
        nodes: vec!["0".into(), "out".into()],
        elements: vec![
            Element::Current {
                id: "I1".into(),
                p: NodeId(0),
                n: NodeId(1),
                amps,
            },
            Element::Diode {
                id: "D1".into(),
                p: NodeId(1),
                n: NodeId(0),
                saturation_amps: 1e-12,
                thermal_volts: 0.02585,
            },
        ],
    }
}

fn source_policy() -> ContinuationOptions {
    ContinuationOptions {
        initial_step: 0.125,
        minimum_step: 1e-6,
        max_stages: 128,
    }
}

fn gmin_policy() -> GminContinuationOptions {
    GminContinuationOptions {
        initial_siemens: 1.,
        minimum_siemens: 1e-6,
        reduction_factor: 0.5,
        max_stages: 32,
    }
}

// Check physical KCL independently, without calling the production residual/model.
// The 1e-6 S final warm-start shunt carries about 0.35 uA: omitting its removal
// must fail these bounds even if the result report claims an unregularized solve.
fn assert_physical(values: &[f64], amps: f64) {
    let expected = 0.02585 * (amps / 1e-12).ln_1p();
    assert!(
        (values[0] - expected).abs() < 2e-7,
        "voltage: {values:?}, I={amps}"
    );
    let mismatch = 1e-12 * (values[0] / 0.02585).exp_m1() - amps;
    assert!(mismatch.abs() < 2e-12, "physical KCL: {mismatch}, I={amps}");
}

#[test]
fn continuation_recovers_current_driven_diode_across_cpu_providers() {
    let plan = compile(&current_diode(1e-6)).unwrap();
    for solver in [&DenseLu as &dyn LinearSolver, &DenseQr, &FaerSparseLu] {
        let base = NewtonOptions {
            max_iterations: 8,
            ..NewtonOptions::default()
        };
        assert_eq!(
            solve(&plan, solver, base).unwrap_err().code,
            ErrorCode::NumericalFailure
        );
        let (values, report) = solve(
            &plan,
            solver,
            NewtonOptions {
                continuation: Some(source_policy()),
                ..base
            },
        )
        .unwrap();
        assert_physical(&values, 1e-6);
        let stages = report.continuation.unwrap();
        assert!(stages.rejected_stages > 0);
        assert!(stages.accepted_stages + stages.rejected_stages <= 128);
        assert_eq!(stages.stages.last().unwrap().source_scale, 1.);
        assert!(
            stages
                .stages
                .windows(2)
                .all(|w| w[0].source_scale < w[1].source_scale)
        );
        let (values, report) = solve(
            &plan,
            solver,
            NewtonOptions {
                gmin_continuation: Some(gmin_policy()),
                ..base
            },
        )
        .unwrap();
        assert_physical(&values, 1e-6);
        assert_eq!(report.gmin_siemens, None);
        let stages = report.gmin_continuation.unwrap();
        assert!(stages.final_unregularized);
        assert_eq!(stages.stages.first().unwrap().gmin_siemens, 1.);
        assert_eq!(stages.stages.last().unwrap().gmin_siemens, 1e-6);
        assert!(stages.stages.len() <= 32);
        assert!(
            stages
                .stages
                .windows(2)
                .all(|w| w[0].gmin_siemens > w[1].gmin_siemens)
        );
    }
}

#[test]
fn gmin_cannot_publish_a_regularized_solution_to_an_impossible_physical_bias() {
    // Reverse current exceeds Is: a shunt can carry it, but the physical diode cannot.
    let plan = compile(&current_diode(-1e-6)).unwrap();
    let base = NewtonOptions::default();
    assert!(
        solve(
            &plan,
            &DenseLu,
            NewtonOptions {
                gmin_siemens: Some(1e-3),
                ..base
            }
        )
        .is_ok()
    );
    let error = solve(
        &plan,
        &DenseLu,
        NewtonOptions {
            gmin_continuation: Some(GminContinuationOptions {
                initial_siemens: 1e-3,
                minimum_siemens: 1e-3,
                reduction_factor: 0.5,
                max_stages: 1,
            }),
            ..base
        },
    )
    .unwrap_err();
    assert_eq!(error.code, ErrorCode::NumericalFailure);
}

#[test]
fn continuation_stage_budgets_fail_without_a_partial_success() {
    let plan = compile(&current_diode(1e-6)).unwrap();
    let error = solve(
        &plan,
        &DenseLu,
        NewtonOptions {
            gmin_continuation: Some(GminContinuationOptions {
                max_stages: 1,
                ..gmin_policy()
            }),
            ..NewtonOptions::default()
        },
    )
    .unwrap_err();
    assert_eq!(error.code, ErrorCode::NumericalFailure);
    assert!(error.message.contains("stage budget exhausted"));
    let error = solve(
        &plan,
        &DenseLu,
        NewtonOptions {
            continuation: Some(ContinuationOptions {
                max_stages: 1,
                ..source_policy()
            }),
            ..NewtonOptions::default()
        },
    )
    .unwrap_err();
    assert_eq!(error.code, ErrorCode::NumericalFailure);
    assert!(error.message.contains("exhausted"));
}

#[test]
fn seeded_continuation_corpus_matches_inverse_shockley_and_physical_kcl() {
    // Fixed LCG seed, 64 log-uniform currents in [0.1, 10] uA, both policies.
    // This is a bounded property corpus, not coverage-guided fuzzing or broad convergence evidence.
    let mut state = 0x43_4f_4e_54_49_4e_55_45_u64;
    for _ in 0..64 {
        state = state.wrapping_mul(6364136223846793005).wrapping_add(1);
        let unit = ((state >> 11) as f64) / ((1u64 << 53) as f64);
        let amps = 10f64.powf(-7. + 2. * unit);
        let plan = compile(&current_diode(amps)).unwrap();
        for options in [
            NewtonOptions {
                continuation: Some(source_policy()),
                ..NewtonOptions::default()
            },
            NewtonOptions {
                gmin_continuation: Some(gmin_policy()),
                ..NewtonOptions::default()
            },
        ] {
            let (values, _) = solve(&plan, &DenseLu, options).unwrap();
            assert_physical(&values, amps);
        }
    }
}

#[test]
fn malformed_gmin_stage_policies_are_rejected() {
    for value in [f64::NAN, f64::INFINITY, -1., 0.] {
        assert!(
            GminContinuationOptions {
                initial_siemens: value,
                ..gmin_policy()
            }
            .validate()
            .is_err()
        );
        assert!(
            GminContinuationOptions {
                minimum_siemens: value,
                ..gmin_policy()
            }
            .validate()
            .is_err()
        );
    }
    for factor in [f64::NAN, f64::INFINITY, -1., 1., 2.] {
        assert!(
            GminContinuationOptions {
                reduction_factor: factor,
                ..gmin_policy()
            }
            .validate()
            .is_err()
        );
    }
    for count in [0, 129, usize::MAX] {
        assert!(
            GminContinuationOptions {
                max_stages: count,
                ..gmin_policy()
            }
            .validate()
            .is_err()
        );
    }
}
