//! Independent special-angle, boundary and invalid-domain checks for AM/SFFM.
use circuit_simulator::waveform::Waveform;
fn fm() -> serde_json::Value {
    serde_json::json!({"kind":"frequency_modulated","offset":0.5,"amplitude":2.,"carrier_hz":1.,"modulation_index":0.,"modulation_hz":1.,"delay_s":1.,"modulation_phase_rad":0.,"carrier_phase_rad":0.})
}
fn am() -> serde_json::Value {
    serde_json::json!({"kind":"amplitude_modulated","offset":0.5,"modulation_offset":2.,"modulation_amplitude":3.,"modulation_hz":1.,"carrier_hz":1.,"delay_s":1.,"modulation_phase_rad":0.,"carrier_phase_rad":0.})
}
#[test]
fn special_angles_and_inclusive_delay_follow_the_declared_quantity() {
    for (json, expected) in [(fm(), 2.5), (am(), 5.5)] {
        let wave: Waveform = serde_json::from_value(json).unwrap();
        assert_eq!(wave.at(0.).unwrap(), 0.);
        assert_eq!(wave.at(1.).unwrap(), 0.);
        assert!((wave.at(1.25).unwrap() - expected).abs() < 1e-14);
        assert_eq!(wave.breakpoints(), vec![1.]);
    }
}
#[test]
fn invalid_frequency_delay_and_phase_overflow_are_explicit_errors() {
    for mut json in [fm(), am()] {
        json["carrier_hz"] = serde_json::json!(0.);
        assert!(
            serde_json::from_value::<Waveform>(json)
                .unwrap()
                .validate()
                .is_err()
        );
    }
    let mut json = fm();
    json["delay_s"] = serde_json::json!(-1.);
    assert!(
        serde_json::from_value::<Waveform>(json)
            .unwrap()
            .validate()
            .is_err()
    );
    let mut json = am();
    json["carrier_hz"] = serde_json::json!(1e308);
    assert!(
        serde_json::from_value::<Waveform>(json)
            .unwrap()
            .at(10.)
            .is_err()
    );
}
#[test]
fn frequency_modulation_preserves_the_signed_inner_phase() {
    let mut json = fm();
    json["modulation_index"] = serde_json::json!(0.5);
    json["carrier_phase_rad"] = serde_json::json!(-std::f64::consts::FRAC_PI_2);
    json["modulation_phase_rad"] = serde_json::json!(0.1);
    let wave: Waveform = serde_json::from_value(json).unwrap();
    let expected = 0.5 + 2. * (0.5 * 0.1_f64.cos()).sin();
    assert!((wave.at(1.25).unwrap() - expected).abs() < 1e-14);
}
proptest::proptest! {
    #![proptest_config(proptest::test_runner::Config {cases:128,rng_seed:proptest::test_runner::RngSeed::Fixed(2026110302),..Default::default()})]
    #[test]
    fn integer_hertz_modulation_is_periodic_after_delay(carrier in 1u32..20,modulation in 1u32..20,t in 1.01f64..4.,offset in -10.0f64..10.) {
        for mut json in [fm(),am()] {
            json["carrier_hz"]=serde_json::json!(carrier);json["modulation_hz"]=serde_json::json!(modulation);json["offset"]=serde_json::json!(offset);
            if json.get("modulation_index").is_some(){json["modulation_index"]=serde_json::json!(0.25*f64::from(carrier)/f64::from(modulation));}
            let wave:Waveform=serde_json::from_value(json).unwrap();
            proptest::prop_assert!((wave.at(t).unwrap()-wave.at(t+1.).unwrap()).abs()<1e-10);
        }
    }
}
