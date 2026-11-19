use circuit_simulator::{
    api::{AcSourcePhasor, Analysis, Request, run},
    assertion::{Outcome, within},
    grid::log_grid,
    measurement::value_at,
    netlist::parse,
    quantity::spice_scalar,
    transient::Sample,
};

#[test]
fn nonfinite_measurements_are_indeterminate_even_with_valid_finite_bounds() {
    for value in [f64::NAN, f64::INFINITY, f64::NEG_INFINITY] {
        assert_eq!(within(Some(value), -1., 1.), Outcome::Indeterminate);
    }
    assert_eq!(within(Some(-1.), -1., 1.), Outcome::Pass);
    assert_eq!(within(Some(1.), -1., 1.), Outcome::Pass);
    assert_eq!(within(Some(2.), -1., 1.), Outcome::Fail);
}
#[test]
fn rc_ac_matches_complex_transfer() {
    let c = parse("RC\nV1 in 0 1\nR1 in out 1k\nC1 out 0 1u\n.end").unwrap();
    let r = run(&Request {
        schema_version: "1".into(),
        case_id: "ac".into(),
        circuit: c,
        analysis: Analysis::Ac {
            frequencies_hz: vec![1.0 / (std::f64::consts::TAU * 0.001)],
            source_phasors: vec![],
        },
        backend: "lu".into(),
        experiment: None,
    })
    .unwrap();
    let v = &r.ac_samples[0].values[1];
    assert!((v.real - 0.5).abs() < 1e-12);
    assert!((v.imag + 0.5).abs() < 1e-12);
    assert_eq!(r.request_sha256.len(), 64);
}

#[test]
fn ac_source_phasors_reject_non_source_targets_before_solving() {
    let circuit = parse("title\nR1 out 0 1k\n.end").unwrap();
    let request = Request {
        schema_version: "1".into(),
        case_id: "invalid-ac-phasor".into(),
        circuit,
        analysis: Analysis::Ac {
            frequencies_hz: vec![1.],
            source_phasors: vec![AcSourcePhasor {
                id: "R1".into(),
                real: 1.,
                imag: 0.,
            }],
        },
        backend: "lu".into(),
        experiment: None,
    };
    assert_eq!(
        run(&request).unwrap_err().code,
        circuit_simulator::error::ErrorCode::InvalidInput
    );
}

#[test]
fn ac_source_phasors_superpose_real_and_quadrature_sources() {
    let circuit = parse("title\nV1 out 0 0\nI1 0 out 0\nR1 out 0 1\n.end").unwrap();
    let request = Request {
        schema_version: "1".into(),
        case_id: "phasor-superposition".into(),
        circuit,
        analysis: Analysis::Ac {
            frequencies_hz: vec![1.],
            source_phasors: vec![
                AcSourcePhasor {
                    id: "V1".into(),
                    real: 2.,
                    imag: 3.,
                },
                AcSourcePhasor {
                    id: "I1".into(),
                    real: 1.,
                    imag: -1.,
                },
            ],
        },
        backend: "lu".into(),
        experiment: None,
    };
    let result = run(&request).unwrap();
    // The ideal voltage source fixes v(out), while the current source affects its branch current.
    assert!((result.ac_samples[0].values[0].real - 2.).abs() < 1e-12);
    assert!((result.ac_samples[0].values[0].imag - 3.).abs() < 1e-12);
}
#[test]
fn current_source_polarity() {
    let c = parse("test\nI1 0 a 1m\nR1 a 0 1k\n.end").unwrap();
    let r = run(&Request {
        schema_version: "1".into(),
        case_id: "i".into(),
        circuit: c,
        analysis: Analysis::OperatingPoint,
        backend: "lu".into(),
        experiment: None,
    })
    .unwrap();
    assert!((r.samples[0].values[0] - 1.0).abs() < 1e-12);
}
#[test]
fn units_suffixes() {
    assert_eq!(spice_scalar("1M").unwrap(), 0.001);
    assert_eq!(spice_scalar("1MEG").unwrap(), 1e6);
    assert!(spice_scalar("NaN").is_err());
    assert!(spice_scalar("1junk").is_err());
}
#[test]
fn directives_are_not_commands() {
    assert!(parse("title\n.control\nshell whoami\n.endc").is_err());
    assert!(parse("title\nR1 a 0 nope").is_err());
}
#[test]
fn grid_is_monotonic_and_bounded() {
    let x = log_grid(1.0, 1000.0, 31).unwrap();
    assert_eq!(*x.last().unwrap(), 1000.0);
    assert!(x.windows(2).all(|w| w[0] < w[1]));
    assert!(log_grid(0.0, 10.0, 3).is_err());
    assert!(log_grid(1.0, 10.0, 10001).is_err());
}
#[test]
fn interpolated_measurement_and_missing_policy() {
    let s = vec![
        Sample {
            time_s: 1.0,
            values: vec![2.0],
        },
        Sample {
            time_s: 3.0,
            values: vec![6.0],
        },
    ];
    assert_eq!(value_at(&s, 0, 2.0).unwrap(), 4.0);
    assert!(value_at(&s, 0, 0.0).is_err());
    assert_eq!(within(None, 0.0, 1.0), Outcome::Indeterminate);
    assert_eq!(within(Some(1.0), 0.0, 1.0), Outcome::Pass);
    assert_eq!(within(Some(2.0), 0.0, 1.0), Outcome::Fail);
}

#[test]
fn logarithmic_grid_interior_points_match_independent_geometric_sequences() {
    let decade = log_grid(1., 1000., 4).unwrap();
    for (actual, expected) in decade.iter().zip([1., 10., 100., 1000.]) {
        assert!(
            (actual / expected - 1.).abs() < 2e-13,
            "{actual} vs {expected}"
        );
    }
    for scale in [1e-12, 1., 1e12] {
        let grid = log_grid(scale, 8. * scale, 4).unwrap();
        assert_eq!(grid.len(), 4);
        assert_eq!(*grid.last().unwrap(), 8. * scale);
        for (index, actual) in grid.iter().enumerate() {
            let expected = scale * 2_f64.powi(index as i32);
            assert!(
                (actual / expected - 1.).abs() < 2e-13,
                "{actual} vs {expected}"
            );
        }
    }
}

#[test]
fn native_subset_netlist_byte_limit_includes_the_exact_maximum() {
    const BASE: &str = "RC\nV1 in 0 10\nR1 in out 1k\nR2 out 0 1k\n.end\n";
    let reference = serde_json::to_value(parse(BASE).unwrap()).unwrap();
    for bytes in [1_048_575, 1_048_576, 1_048_577] {
        let mut text = String::with_capacity(bytes);
        text.push_str(BASE);
        // Blank trailing input is legal and changes no circuit semantics. It
        // isolates the whole-input byte limit from syntax/topology failures.
        text.extend(std::iter::repeat_n(' ', bytes - BASE.len()));
        assert_eq!(text.len(), bytes);
        let result = parse(&text);
        if bytes == 1_048_577 {
            assert_eq!(
                result.err().expect("oversized input must fail").code,
                circuit_simulator::error::ErrorCode::ResourceExhausted
            );
        } else {
            assert_eq!(serde_json::to_value(result.unwrap()).unwrap(), reference);
        }
    }
}
