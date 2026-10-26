//! Independent boundary and difficult-topology evidence for bounded Newton paths.
use circuit_simulator::newton::norm_inf;
use circuit_simulator::newton::{ContinuationOptions, GminContinuationOptions, NewtonOptions};
use circuit_simulator::{
    circuit::{Circuit, Element},
    compile::{CompiledCircuit, compile},
    error::{ErrorCode, SimResult},
    ids::NodeId,
    linalg::{LinearSolver, Matrix},
    newton::{solve, solve_backward_euler_step},
};

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
