use circuit_simulator::{
    adaptive::AdaptiveOptions,
    backend_faer::FaerSparseLu,
    compile::compile,
    configured::{self, Method, TransientConfig},
    linalg::{LinearSolver, Matrix},
    netlist::parse,
    waveform::{Point, Waveform},
};
#[test]
fn pwl_interpolation_is_linear_between_corners() {
    let w = Waveform::Pwl {
        points: vec![
            Point {
                time_s: 0.,
                value: 0.,
            },
            Point {
                time_s: 1.,
                value: 2.,
            },
            Point {
                time_s: 2.,
                value: 0.,
            },
        ],
    };
    assert_eq!(w.at(0.5).unwrap(), 1.);
    assert_eq!(w.at(1.5).unwrap(), 1.);
    assert_eq!(w.at(3.).unwrap(), 0.);
}
#[test]
fn sparse_singular_and_nonfinite_inputs_fail() {
    assert!(
        FaerSparseLu
            .solve(
                &Matrix {
                    n: 2,
                    values: vec![0.; 4]
                },
                &[0., 0.]
            )
            .is_err()
    );
    assert!(
        FaerSparseLu
            .solve(
                &Matrix {
                    n: 1,
                    values: vec![f64::NAN]
                },
                &[1.]
            )
            .is_err()
    );
}
#[test]
fn strict_minimum_step_cannot_falsely_succeed() {
    let p = compile(&parse("title\nR1 a 0 1k\nC1 a 0 1u").unwrap()).unwrap();
    let c = TransientConfig {
        stop_s: 0.005,
        max_step_s: 0.001,
        method: Method::Trapezoidal,
        initial_state: Some(vec![1.]),
        adaptive: Some(AdaptiveOptions {
            relative: 1e-12,
            voltage_absolute: 1e-15,
            current_absolute: 1e-15,
            min_step_s: 0.001,
            max_attempts: 10,
        }),
        ..Default::default()
    };
    assert!(configured::run(&p, &FaerSparseLu, &c, &false.into()).is_err());
}
