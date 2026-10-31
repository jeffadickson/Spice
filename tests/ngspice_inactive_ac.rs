//! Cross-analysis source metadata must not change a constant DC excitation.
use circuit_simulator::{
    api::{Analysis, run},
    netlist::import_ngspice_request,
};
use proptest::prelude::*;
use proptest::test_runner::{Config, RngSeed};

#[test]
fn active_ac_retains_dc_bias_and_both_phasor_components() {
    for (source, amplitude, voltage_sign) in [
        ("V1 out 0 DC 2 AC 3 45", 3., 1.),
        ("I1 out 0 DC 2m AC 3m 45", 0.003, -1000.),
    ] {
        let request = import_ngspice_request(
            &format!("owned\n{source}\nR1 out 0 1000\n.ac lin 1 10 10\n.end"),
            "active-phase",
        )
        .unwrap();
        if let Analysis::Ac { source_phasors, .. } = &request.analysis {
            assert!((source_phasors[0].real - amplitude / std::f64::consts::SQRT_2).abs() < 1e-14);
            assert!((source_phasors[0].imag - amplitude / std::f64::consts::SQRT_2).abs() < 1e-14);
        } else {
            panic!("expected AC");
        }
        let dc = request
            .circuit
            .elements
            .iter()
            .find_map(|element| match element {
                circuit_simulator::circuit::Element::Voltage { volts, .. } => Some(*volts),
                circuit_simulator::circuit::Element::Current { amps, .. } => Some(*amps),
                _ => None,
            })
            .unwrap();
        assert!((dc - if voltage_sign > 0. { 2. } else { 0.002 }).abs() < 1e-14);
        let expected = voltage_sign * amplitude / std::f64::consts::SQRT_2;
        let output = run(&request).unwrap();
        let value = &output.ac_samples[0].values[0];
        assert!((value.real - expected).abs() < 1e-12);
        assert!((value.imag - expected).abs() < 1e-12);
    }
}

proptest! {
    #![proptest_config(Config{cases:256,rng_seed:RngSeed::Fixed(0x5350494345444341),..Config::default()})]
    #[test]
    fn inactive_metadata_never_changes_the_constant_bias(value in -1000i32..1000, current in any::<bool>(), transient in any::<bool>()) {
        let id=if current {"I1"} else {"V1"};
        let analysis=if transient {".tran 1m 2m"} else {".op"};
        let request=import_ngspice_request(&format!("owned\n{id} out 0 DC {value}e-3 AC 7 91\nR1 out 0 1000\n{analysis}\n.end"),"bias-property").unwrap();
        let expected=if current {-f64::from(value)} else {f64::from(value)*1e-3};
        for sample in run(&request).unwrap().samples {prop_assert!((sample.values[0]-expected).abs()<1e-9);}
    }
}

#[test]
fn source_suffix_a_is_atto_not_an_ampere_annotation() {
    let request = import_ngspice_request(
        "owned\nV1 out 0 DC 0 AC 1A\nR1 out 0 1000\n.ac lin 1 10 10\n.end",
        "atto",
    )
    .unwrap();
    match &request.analysis {
        Analysis::Ac { source_phasors, .. } => assert_eq!(source_phasors[0].real, 1e-18),
        _ => panic!("expected AC"),
    }
    let output = run(&request).unwrap();
    assert!((output.ac_samples[0].values[0].real / 1e-18 - 1.).abs() < 1e-12);
    let current = import_ngspice_request(
        "owned\nI1 out 0 DC 0 AC 1a\nR1 out 0 1000\n.ac lin 1 10 10\n.end",
        "atto-current",
    )
    .unwrap();
    assert!((run(&current).unwrap().ac_samples[0].values[0].real / (-1e-15) - 1.).abs() < 1e-12);
    let milli = import_ngspice_request(
        "owned\nI1 out 0 DC 0 AC 2mA 90\nR1 out 0 1000\n.ac lin 1 10 10\n.end",
        "milli-current",
    )
    .unwrap();
    assert!((run(&milli).unwrap().ac_samples[0].values[0].imag + 2.).abs() < 1e-12);
}

#[test]
fn inactive_ac_metadata_preserves_voltage_and_current_dc_in_op_and_transient() {
    for (source, expected_voltage) in [
        ("V1 out 0 DC 2 AC 3 45", 2.),
        ("I1 out 0 DC 2m AC 3m 45", -2.),
        ("V1 out 0 2 AC 3", 2.),
        ("I1 out 0 2m AC 3m", -2.),
    ] {
        for analysis in [".op", ".tran 1m 2m"] {
            let request = import_ngspice_request(
                &format!("owned\n{source}\nR1 out 0 1000\n{analysis}\n.end"),
                "inactive-ac",
            )
            .unwrap();
            if let Analysis::TransientConfigured { options } = &request.analysis {
                assert!(options.sources.is_empty());
                assert_eq!(options.max_step_s, 1e-3);
                assert_eq!(options.stop_s, 2e-3);
            }
            let result = run(&request).unwrap();
            assert!(!result.samples.is_empty());
            for point in result.samples {
                assert!((point.values[0] - expected_voltage).abs() < 1e-12);
            }
        }
    }
}

#[test]
fn source_shaped_title_is_never_evaluated_as_a_source() {
    for title in [
        "Vtitle out 0 DC NaN AC NaN",
        "Vtitle out 0 PULSE(0,2,0,1m,1m,2m,6m)",
        ".tran nonsense nope",
    ] {
        let request = import_ngspice_request(
            &format!("{title}\nV1 out 0 DC 2 AC 3 45\nR1 out 0 1000\n.ac lin 1 10 10\n.end"),
            "title",
        )
        .unwrap();
        match request.analysis {
            Analysis::Ac { source_phasors, .. } => assert_eq!(source_phasors.len(), 1),
            _ => panic!("expected AC"),
        }
    }
    for analysis in [".op", ".tran 1m 2m", ".ac lin 1 10 10"] {
        let request=import_ngspice_request(&format!("owned\nV1 out 0 DC 2 AC 3 45 ; ignored report words\nR1 out 0 1000\n{analysis}\n.end"),"comment-metadata").unwrap();
        assert!(!request.circuit.elements.is_empty());
    }
}

#[test]
fn inactive_metadata_does_not_erase_invalid_parameters_or_unknown_auxiliaries() {
    for tail in [
        "DC 2 AC NaN",
        "DC 2 AC 1 infinity",
        "DC 2 AC 1 AC 2",
        "DC 2 AC 1 SIN(0,1,1000)",
        "DC NaN AC 1",
        "2 AC 1 AC 2",
    ] {
        for analysis in [".op", ".tran 1m 2m"] {
            assert!(
                import_ngspice_request(
                    &format!("owned\nV1 out 0 {tail}\nR1 out 0 1000\n{analysis}\n.end"),
                    "invalid-ac"
                )
                .is_err(),
                "accepted {tail}"
            );
        }
    }
}
