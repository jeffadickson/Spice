//! Independent boundary and difficult-topology evidence for bounded Newton paths.
use circuit_simulator::newton::linearize_backward_euler;
use circuit_simulator::newton::solve_from;
use std::sync::Mutex;
use std::sync::atomic::AtomicUsize;

struct ObserveFirstTrial(Mutex<Vec<f64>>);
impl LinearSolver for ObserveFirstTrial {
    fn name(&self) -> &'static str {
        "trial-observer"
    }
    fn solve(&self, matrix: &Matrix, rhs: &[f64]) -> SimResult<Vec<f64>> {
        let mut seen = self.0.lock().unwrap();
        seen.push(matrix.values[0]);
        if seen.len() == 2 {
            return Err(circuit_simulator::error::SimError::new(
                ErrorCode::Cancelled,
                "fixture stops after observing the first accepted trial",
            ));
        }
        DenseLu.solve(matrix, rhs)
    }
}

#[test]
fn accepted_initial_diode_trial_respects_two_thermal_voltage_limit() {
    for thermal in [1e-5, 0.02585, 0.1] {
        let plan = compile(&Circuit {
            nodes: vec!["0".into(), "x".into()],
            elements: vec![
                Element::Current {
                    id: "I".into(),
                    p: NodeId(0),
                    n: NodeId(1),
                    amps: 1e-6,
                },
                Element::Diode {
                    id: "D".into(),
                    p: NodeId(1),
                    n: NodeId(0),
                    saturation_amps: 1e-12,
                    thermal_volts: thermal,
                },
            ],
        })
        .unwrap();
        let observer = ObserveFirstTrial(Mutex::new(Vec::new()));
        assert_eq!(
            solve(&plan, &observer, NewtonOptions::default())
                .unwrap_err()
                .code,
            ErrorCode::Cancelled
        );
        let observed = observer.0.lock().unwrap();
        assert_eq!(observed.len(), 2);
        // For one diode, J=Is*exp(V/Vt)/Vt. The ratio independently
        // reconstructs the first accepted exponent increment.
        assert!((observed[1] / observed[0] - 2_f64.exp()).abs() < 1e-12);
    }
}
use std::sync::atomic::{AtomicBool, Ordering};

struct ObservedSolves(AtomicUsize);
impl LinearSolver for ObservedSolves {
    fn name(&self) -> &'static str {
        "observed-dense-lu"
    }
    fn solve(&self, matrix: &Matrix, rhs: &[f64]) -> SimResult<Vec<f64>> {
        self.0.fetch_add(1, Ordering::Relaxed);
        DenseLu.solve(matrix, rhs)
    }
}

#[test]
fn continuation_work_counts_all_provider_calls_including_recovery() {
    let plan = diode_plan();
    for options in [
        NewtonOptions {
            continuation: Some(ContinuationOptions::default()),
            ..NewtonOptions::default()
        },
        NewtonOptions {
            gmin_continuation: Some(GminContinuationOptions::default()),
            ..NewtonOptions::default()
        },
    ] {
        let solver = ObservedSolves(AtomicUsize::new(0));
        let (_, report) = solve(&plan, &solver, options).unwrap();
        assert_eq!(report.linear_solves, solver.0.load(Ordering::Relaxed));
        assert!(report.linear_solves > report.iterations);
    }
}

struct OneOvershoot {
    first: AtomicBool,
    factor: f64,
}
impl LinearSolver for OneOvershoot {
    fn name(&self) -> &'static str {
        "one-overshoot-fixture"
    }
    fn solve(&self, matrix: &Matrix, rhs: &[f64]) -> SimResult<Vec<f64>> {
        let mut correction = DenseLu.solve(matrix, rhs)?;
        if self.first.swap(false, Ordering::Relaxed) {
            for value in &mut correction {
                *value *= self.factor;
            }
        }
        Ok(correction)
    }
}

#[test]
fn backtracking_contracts_a_prelimited_direction_and_rejects_overflow_trials() {
    // An intentionally oversized first direction challenges line search; the
    // final state still has to satisfy an independent physical equation.
    let plan = diode_plan();
    let expected = 0.02585 * (1e6_f64).ln_1p();
    let solver = OneOvershoot {
        first: AtomicBool::new(true),
        factor: 1e30,
    };
    let (values, _) = solve_from(
        &plan,
        &solver,
        NewtonOptions::default(),
        vec![expected - 0.02585 * 4_f64.ln()],
    )
    .unwrap();
    assert!((1e-12 * (values[0] / 0.02585).exp_m1() - 1e-6).abs() < 2e-12);

    let linear = compile(&Circuit {
        nodes: vec!["0".into(), "x".into()],
        elements: vec![
            Element::Resistor {
                id: "R".into(),
                p: NodeId(1),
                n: NodeId(0),
                ohms: 1.,
            },
            Element::Current {
                id: "I".into(),
                p: NodeId(0),
                n: NodeId(1),
                amps: 1.3e308,
            },
        ],
    })
    .unwrap();
    let solver = OneOvershoot {
        first: AtomicBool::new(true),
        factor: 4.,
    };
    let (values, _) = solve_from(&linear, &solver, NewtonOptions::default(), vec![9e307]).unwrap();
    assert!(((values[0] - 1.3e308) / 1.3e308).abs() < 1e-12);
}

#[test]
fn thermal_scaled_damping_recovers_direct_bias_over_a_bounded_vt_range() {
    for thermal in [1e-5, 1e-4, 1e-3, 0.01, 0.02585, 0.1] {
        let circuit = Circuit {
            nodes: vec!["0".into(), "out".into()],
            elements: vec![
                Element::Current {
                    id: "I".into(),
                    p: NodeId(0),
                    n: NodeId(1),
                    amps: 1e-6,
                },
                Element::Diode {
                    id: "D".into(),
                    p: NodeId(1),
                    n: NodeId(0),
                    saturation_amps: 1e-12,
                    thermal_volts: thermal,
                },
            ],
        };
        let plan = compile(&circuit).unwrap();
        for initial in [-10. * thermal, 0., 4. * thermal, 20. * thermal] {
            let (values, report) =
                solve_from(&plan, &DenseLu, NewtonOptions::default(), vec![initial]).unwrap();
            let expected = thermal * (1e6_f64).ln_1p();
            assert!(
                (values[0] - expected).abs() < 1e-6 * thermal,
                "Vt={thermal}, initial={initial}"
            );
            assert!((1e-12 * (values[0] / thermal).exp_m1() - 1e-6).abs() < 2e-12);
            assert!(report.iterations <= 50);
        }
    }
}
use circuit_simulator::newton::norm_inf;
use circuit_simulator::newton::{ContinuationOptions, GminContinuationOptions, NewtonOptions};
use circuit_simulator::{backend::DenseLu, newton::solve_with_rhs};

#[test]
fn be_rejects_nonfinite_history_and_sources_before_trial_evaluation() {
    let plan = diode_plan();
    for bad in [f64::NAN, f64::INFINITY, f64::NEG_INFINITY] {
        for (previous, rhs) in [(vec![bad], vec![0.]), (vec![0.], vec![bad])] {
            let before = previous[0].to_bits();
            let error = linearize_backward_euler(&plan, &[0.], &previous, 1e-3, &rhs).unwrap_err();
            assert_eq!(error.code, ErrorCode::InvalidInput);
            assert!(error.message.contains("step inputs"));
            assert_eq!(previous[0].to_bits(), before);
        }
    }
}

#[test]
fn be_rhs_and_displacement_avoid_spurious_intermediate_overflow() {
    let resistive = compile(&Circuit {
        nodes: vec!["0".into(), "x".into()],
        elements: vec![
            Element::Resistor {
                id: "R".into(),
                p: NodeId(1),
                n: NodeId(0),
                ohms: 1.,
            },
            Element::Current {
                id: "I".into(),
                p: NodeId(0),
                n: NodeId(1),
                amps: 1e308,
            },
        ],
    })
    .unwrap();
    let system = linearize_backward_euler(&resistive, &[0.], &[0.], 1., &[-1e308]).unwrap();
    assert_eq!(system.residual[0], 1e308);
    let zero_charge =
        linearize_backward_euler(&resistive, &[1e308], &[-1e308], 1., &[1e308]).unwrap();
    assert_eq!(zero_charge.residual[0], 0.);
    let capacitive = compile(&Circuit {
        nodes: vec!["0".into(), "x".into()],
        elements: vec![Element::Capacitor {
            id: "C".into(),
            p: NodeId(1),
            n: NodeId(0),
            farads: 1e-308,
        }],
    })
    .unwrap();
    let displacement =
        linearize_backward_euler(&capacitive, &[1e308], &[-1e308], 1., &[0.]).unwrap();
    assert!((displacement.residual[0] - 2.).abs() < 1e-14);
}
use circuit_simulator::{
    circuit::{Circuit, Element},
    compile::{CompiledCircuit, compile},
    error::{ErrorCode, SimResult},
    ids::NodeId,
    linalg::{LinearSolver, Matrix},
    newton::{solve, solve_backward_euler_step},
};

#[test]
fn dc_recovery_policies_are_not_silently_ignored_by_other_entry_points() {
    let plan = diode_plan();
    let recovery = [
        NewtonOptions {
            continuation: Some(ContinuationOptions::default()),
            ..NewtonOptions::default()
        },
        NewtonOptions {
            gmin_continuation: Some(GminContinuationOptions::default()),
            ..NewtonOptions::default()
        },
    ];
    for options in recovery {
        assert_eq!(
            solve_with_rhs(&plan, &DenseLu, options, vec![0.], plan.rhs())
                .unwrap_err()
                .code,
            ErrorCode::Unsupported
        );
        assert_eq!(
            solve_backward_euler_step(&plan, &DenseLu, options, &[0.], 1e-3, plan.rhs())
                .unwrap_err()
                .code,
            ErrorCode::Unsupported
        );
    }
    let fixed = NewtonOptions {
        gmin_siemens: Some(1e-3),
        ..NewtonOptions::default()
    };
    assert_eq!(
        solve_backward_euler_step(&plan, &DenseLu, fixed, &[0.], 1e-3, plan.rhs())
            .unwrap_err()
            .code,
        ErrorCode::Unsupported
    );
    // Fixed, explicitly recorded regularization remains meaningful for a
    // caller-supplied DC excitation and must not be rejected with continuation.
    let (_, report) = solve_with_rhs(&plan, &DenseLu, fixed, vec![0.], plan.rhs()).unwrap();
    assert_eq!(report.gmin_siemens, Some(1e-3));
}

fn diode_plan() -> CompiledCircuit {
    compile(&Circuit {
        nodes: vec!["0".into(), "out".into()],
        elements: vec![
            Element::Current {
                id: "I1".into(),
                p: NodeId(0),
                n: NodeId(1),
                amps: 1e-6,
            },
            Element::Diode {
                id: "D1".into(),
                p: NodeId(1),
                n: NodeId(0),
                saturation_amps: 1e-12,
                thermal_volts: 0.02585,
            },
        ],
    })
    .unwrap()
}

struct BadCorrection(u8);
impl LinearSolver for BadCorrection {
    fn name(&self) -> &'static str {
        "malformed-correction-fixture"
    }
    fn solve(&self, matrix: &Matrix, _rhs: &[f64]) -> SimResult<Vec<f64>> {
        Ok(match self.0 {
            0 => vec![],
            1 => vec![0.; matrix.n + 1],
            2 => vec![f64::NAN; matrix.n],
            _ => vec![f64::INFINITY; matrix.n],
        })
    }
}

#[test]
fn malformed_provider_corrections_fail_dc_and_be_without_panic() {
    let plan = diode_plan();
    for kind in 0..4 {
        let solver = BadCorrection(kind);
        assert_eq!(
            solve(&plan, &solver, NewtonOptions::default())
                .unwrap_err()
                .code,
            ErrorCode::NumericalFailure
        );
        assert_eq!(
            solve_backward_euler_step(
                &plan,
                &solver,
                NewtonOptions::default(),
                &[0.],
                1e-3,
                plan.rhs()
            )
            .unwrap_err()
            .code,
            ErrorCode::NumericalFailure
        );
    }
}

#[test]
fn nested_source_policies_are_validated_at_the_options_boundary() {
    for bad in [0., -1., f64::NAN, f64::INFINITY, 1.1] {
        assert!(
            NewtonOptions {
                continuation: Some(ContinuationOptions {
                    initial_step: bad,
                    ..ContinuationOptions::default()
                }),
                ..NewtonOptions::default()
            }
            .validate()
            .is_err()
        );
    }
    for count in [0, 1025, usize::MAX] {
        assert!(
            NewtonOptions {
                continuation: Some(ContinuationOptions {
                    max_stages: count,
                    ..ContinuationOptions::default()
                }),
                ..NewtonOptions::default()
            }
            .validate()
            .is_err()
        );
    }
}

#[test]
fn gmin_reduction_requires_an_open_positive_unit_interval() {
    for bad in [-0., 0., -1., 1., f64::NAN, f64::INFINITY] {
        assert!(
            GminContinuationOptions {
                reduction_factor: bad,
                ..GminContinuationOptions::default()
            }
            .validate()
            .is_err()
        );
    }
    for good in [f64::MIN_POSITIVE, 0.1, 0.5, 1. - f64::EPSILON] {
        assert!(
            GminContinuationOptions {
                reduction_factor: good,
                ..GminContinuationOptions::default()
            }
            .validate()
            .is_ok()
        );
    }
}

#[test]
fn nonfinite_norm_components_cannot_disappear_in_a_maximum() {
    for bad in [
        f64::NAN,
        f64::INFINITY,
        f64::NEG_INFINITY,
        f64::from_bits(0x7ff0_0000_0000_0001),
    ] {
        for values in [vec![bad], vec![bad, 1.], vec![1., bad]] {
            assert!(norm_inf(&values).is_err(), "accepted {values:?}");
        }
    }
    assert_eq!(norm_inf(&[]).unwrap(), 0.);
    assert_eq!(norm_inf(&[-0., 0., -3., 2.]).unwrap(), 3.);
}

#[test]
fn seeded_ieee_norm_boundaries_preserve_nonfinite_rejection() {
    // Replay seed 0x4e4557544f4e3335; raw IEEE patterns include subnormals,
    // infinities and NaNs without depending on a production random generator.
    let mut state = 0x4e45_5754_4f4e_3335_u64;
    for _ in 0..512 {
        state = state.wrapping_mul(6364136223846793005).wrapping_add(1);
        let value = f64::from_bits(state);
        let result = norm_inf(&[1., value, -2.]);
        if value.is_finite() {
            assert_eq!(result.unwrap(), value.abs().max(2.));
        } else {
            assert!(result.is_err(), "seed state {state:016x}");
        }
    }
}
