//! Independent source equations and single-field domains from the unattempted tail.
use circuit_simulator::{
    variation::ScalarSweep,
    waveform::{Point, Waveform},
};

#[test]
fn exponential_level_and_rise_time_have_independent_finite_contracts() {
    let source = |pulsed, rise_delay_s| Waveform::Exponential {
        initial: 2.,
        pulsed,
        rise_delay_s,
        rise_tau_s: 1.,
        fall_delay_s: 10.,
        fall_tau_s: 2.,
    };
    assert!(source(5., 0.).validate().is_ok());
    assert!(source(f64::NAN, 0.).validate().is_err());
    assert!(source(5., f64::NAN).validate().is_err());
}

#[test]
fn damping_and_empty_selection_domains_are_independent() {
    let sine = |damping| Waveform::DampedSine {
        offset: 2.,
        amplitude: 3.,
        frequency_hz: 1.,
        delay_s: 0.25,
        damping_per_s: damping,
        phase_rad: 0.,
    };
    let undamped = sine(0.);
    assert!(undamped.validate().is_ok());
    assert!((undamped.at(0.5).unwrap() - 5.).abs() < 1e-14);
    assert!(sine(-0.1).validate().is_err());
    assert!(sine(f64::NAN).validate().is_err());
    assert_eq!(undamped.breakpoints(), vec![0.25]);
    assert!(
        ScalarSweep {
            element_id: "R1".into(),
            values: vec![]
        }
        .validate()
        .is_err()
    );
}

#[test]
fn source_values_match_independent_nonunit_analytical_cases() {
    let sine = Waveform::Sine {
        offset: 2.,
        amplitude: 3.,
        frequency_hz: 1.,
        phase_rad: 0.,
    };
    assert!((sine.at(0.25).unwrap() - 5.).abs() < 1e-14);
    let pulse = Waveform::Pulse {
        low: 1.,
        high: 5.,
        delay_s: 0.,
        rise_s: 0.25,
        width_s: 0.5,
        fall_s: 2.,
        period_s: 4.,
    };
    assert_eq!(pulse.at(0.125).unwrap(), 3.);
    assert_eq!(pulse.at(0.5).unwrap(), 5.);
    // At t=2 the 2-second fall has progressed 1.25/2 of the four-volt drop.
    assert_eq!(pulse.at(2.).unwrap(), 2.5);
    let exponential = Waveform::Exponential {
        initial: 2.,
        pulsed: 5.,
        rise_delay_s: 0.,
        rise_tau_s: 1.,
        fall_delay_s: 10.,
        fall_tau_s: 2.,
    };
    assert!((exponential.at(1.).unwrap() - (2. + 3. * (1. - (-1_f64).exp()))).abs() < 1e-14);
}

#[test]
fn pwl_uses_local_time_and_rejects_individual_invalid_point_fields() {
    let points = vec![
        Point {
            time_s: 0.,
            value: 1.,
        },
        Point {
            time_s: 2.,
            value: 5.,
        },
        Point {
            time_s: 4.,
            value: 1.,
        },
    ];
    let valid = Waveform::Pwl {
        points: points.clone(),
    };
    assert_eq!(valid.at(3.).unwrap(), 3.);
    assert!(Waveform::Pwl { points: vec![] }.validate().is_err());
    for (time, value) in [(-0.5, 1.), (f64::NAN, 1.), (0., f64::INFINITY)] {
        let mut bad = points.clone();
        bad[0] = Point {
            time_s: time,
            value,
        };
        assert!(Waveform::Pwl { points: bad }.validate().is_err());
    }
}
