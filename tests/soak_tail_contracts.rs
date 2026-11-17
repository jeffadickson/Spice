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

#[test]
fn exponential_delays_admit_equality_and_refuse_reversed_order() {
    let source = |fall_delay_s| Waveform::Exponential {
        initial: 2., pulsed: 5., rise_delay_s: 1., rise_tau_s: 1.,
        fall_delay_s, fall_tau_s: 2.,
    };
    let coincident = source(1.);
    coincident.validate().unwrap();
    assert_eq!(coincident.at(1.).unwrap(), 2.);
    // The two exponentials start together with distinct time constants.
    let expected = 2. + 3. * ((-0.5_f64).exp() - (-1.0_f64).exp());
    assert!((coincident.at(2.).unwrap() - expected).abs() < 1e-14);
    source(1.0_f64.next_up()).validate().unwrap();
    assert!(source(1.0_f64.next_down()).validate().is_err());
}

#[test]
fn sine_phase_retains_its_sign_at_independent_special_angles() {
    for (phase_rad, expected) in [(std::f64::consts::FRAC_PI_2, 5.), (-std::f64::consts::FRAC_PI_2, -1.)] {
        let wave = Waveform::Sine { offset: 2., amplitude: 3., frequency_hz: 1., phase_rad };
        assert!((wave.at(0.).unwrap() - expected).abs() < 1e-14);
        assert!((wave.at(0.5).unwrap() - (4.-expected)).abs() < 1e-14);
    }
}

#[test]
fn damped_sine_multiplies_the_envelope_instead_of_amplifying_it() {
    let wave = Waveform::DampedSine {
        offset: 2., amplitude: 3., frequency_hz: 0.25, delay_s: 0.25,
        damping_per_s: std::f64::consts::LN_2, phase_rad: 0.,
    };
    assert_eq!(wave.at(0.).unwrap(), 2.);
    // One-second half-life at successive positive/negative sine extrema.
    assert!((wave.at(1.25).unwrap()-3.5).abs()<1e-14);
    assert!((wave.at(3.25).unwrap()-1.625).abs()<1e-14);
}

#[test]
fn pulse_fall_endpoint_returns_the_declared_low_level_exactly() {
    let wave = Waveform::Pulse { low: 1., high: 1e16, delay_s: 0.,
        rise_s: 0.25, width_s: 0.5, fall_s: 0.25, period_s: 2. };
    wave.validate().unwrap();
    // At the exact end, interpolating high+(low-high) loses the unit low level.
    // The settled segment owns this breakpoint and must return the declared low.
    assert_eq!(wave.at(1.).unwrap(), 1.);
    assert_eq!(wave.at(1.0_f64.next_up()).unwrap(), 1.);
}
