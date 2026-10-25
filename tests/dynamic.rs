use circuit_simulator::{
    api::{Analysis, Request, run},
    backend::DenseLu,
    compile::compile,
    configured::{self, Method, TransientConfig},
    netlist::parse,
    sources::SourceOverride,
    waveform::{Point, Waveform},
};
#[test]
fn inductor_is_short_at_dc_and_has_signed_current() {
    let c = parse("title\nV1 in 0 1\nL1 in out 1m\nR1 out 0 100").unwrap();
    let result = run(&Request {
        schema_version: "1".into(),
        case_id: "rl".into(),
        circuit: c,
        analysis: Analysis::OperatingPoint,
        backend: "faer".into(),
        experiment: None,
    })
    .unwrap();
    let index = result.signals.iter().position(|s| s.id == "i(L1)").unwrap();
    assert!((result.samples[0].values[index] - 0.01).abs() < 1e-12);
}
#[test]
fn trapezoidal_rc_discharge_matches_analytic() {
    let p = compile(&parse("title\nR1 a 0 1k\nC1 a 0 1u").unwrap()).unwrap();
    let config = TransientConfig {
        stop_s: 0.005,
        max_step_s: 1e-5,
        method: Method::Trapezoidal,
        initial_state: Some(vec![1.]),
        ..Default::default()
    };
    let result = configured::run(&p, &DenseLu, &config, &false.into()).unwrap();
    assert_eq!(result.samples[0].time_s, 0.);
    assert!((result.samples.last().unwrap().values[0] - (-5_f64).exp()).abs() < 1e-6);
}
#[test]
fn rlc_natural_response_matches_independent_formula() {
    let p = compile(&parse("title\nC1 a 0 1u\nR1 a b 10\nL1 b 0 1m").unwrap()).unwrap();
    let c = TransientConfig {
        stop_s: 0.0002,
        max_step_s: 1e-7,
        method: Method::Trapezoidal,
        initial_state: Some(vec![1., 1., 0.]),
        ..Default::default()
    };
    let result = configured::run(&p, &DenseLu, &c, &false.into()).unwrap();
    let alpha = 5000_f64;
    let wd = (1e9 - alpha * alpha).sqrt();
    let expected =
        (-alpha * c.stop_s).exp() * ((wd * c.stop_s).cos() + alpha / wd * (wd * c.stop_s).sin());
    assert!((result.samples.last().unwrap().values[0] - expected).abs() < 1e-5);
}
#[test]
fn source_corners_are_exact_accepted_points() {
    let p = compile(&parse("title\nV1 a 0 0\nR1 a 0 1k").unwrap()).unwrap();
    let c = TransientConfig {
        stop_s: 0.003,
        max_step_s: 0.002,
        sources: vec![SourceOverride {
            id: "V1".into(),
            waveform: Waveform::Pwl {
                points: vec![
                    Point {
                        time_s: 0.,
                        value: 0.,
                    },
                    Point {
                        time_s: 0.001,
                        value: 1.,
                    },
                    Point {
                        time_s: 0.003,
                        value: 0.,
                    },
                ],
            },
        }],
        ..Default::default()
    };
    let r = configured::run(&p, &DenseLu, &c, &false.into()).unwrap();
    let corner = r.samples.iter().find(|s| s.time_s == 0.001).unwrap();
    assert!((corner.values[0] - 1.).abs() < 1e-12);
}
#[test]
fn invalid_source_and_initial_constraints_fail() {
    assert!(
        Waveform::Pwl {
            points: vec![
                Point {
                    time_s: 1.,
                    value: 0.
                },
                Point {
                    time_s: 0.,
                    value: 1.
                }
            ]
        }
        .validate()
        .is_err()
    );
    let p = compile(&parse("title\nV1 a 0 1\nR1 a 0 1k").unwrap()).unwrap();
    let c = TransientConfig {
        initial_state: Some(vec![0., 0.]),
        ..Default::default()
    };
    assert!(configured::run(&p, &DenseLu, &c, &false.into()).is_err());
    let c = TransientConfig {
        sources: vec![SourceOverride {
            id: "R1".into(),
            waveform: Waveform::Constant { value: 1. },
        }],
        ..Default::default()
    };
    assert!(configured::run(&p, &DenseLu, &c, &false.into()).is_err());
}
