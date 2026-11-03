//! Bounded MOS1 channel must be a genuine nonlinear primitive, not a fixed resistor.
use circuit_simulator::api::{Request, run};
#[test]
fn mos1_channel_dc_uses_gate_and_body_bias() {
    let request = fixture();
    let result = run(&request).unwrap();
    let threshold = 0.7 + 0.4 * (0.8f64.sqrt() - 0.6f64.sqrt());
    let expected = 0.0002 * (1. + 0.03 * 0.5) * 0.5 * (1.5 - threshold - 0.25);
    let index = result.signals.iter().position(|s| s.id == "i(VD)").unwrap();
    assert!((result.samples[0].values[index] + expected).abs() < 1e-12);
}

fn model() -> circuit_simulator::mos1::Mos1Model {
    circuit_simulator::mos1::Mos1Model {
        p_channel: false,
        threshold_volts: 0.7,
        beta_amps_per_volt_squared: 2e-4,
        body_effect_sqrt_volts: 0.4,
        surface_potential_volts: 0.6,
        channel_length_modulation_per_volt: 0.03,
    }
}
#[test]
fn analytic_partials_match_independent_finite_differences_in_all_regions_and_modes() {
    for volts in [
        [0.5, 1.5, 0., -0.2],
        [2., 1.5, 0., -0.2],
        [0.5, 0.2, 0., -0.2],
        [0., 1.5, 0.5, -0.2],
    ] {
        for p_channel in [false, true] {
            let mut m = model();
            m.p_channel = p_channel;
            if p_channel {
                m.threshold_volts = -m.threshold_volts;
            }
            let v = volts.map(|x| if p_channel { -x } else { x });
            let c = m.linearize(v).unwrap();
            assert!(c.partials_siemens.iter().sum::<f64>().abs() < 1e-18);
            for terminal in 0..4 {
                let mut plus = v;
                let mut minus = v;
                plus[terminal] += 1e-6;
                minus[terminal] -= 1e-6;
                let fd = (m.linearize(plus).unwrap().current_amps
                    - m.linearize(minus).unwrap().current_amps)
                    / 2e-6;
                assert!(
                    (fd - c.partials_siemens[terminal]).abs() < 1e-10,
                    "terminal {terminal}: {fd} vs {:?}",
                    c
                );
            }
        }
    }
}
#[test]
fn explicit_domains_reject_bad_parameters_trials_and_forward_body_with_gamma() {
    let m = model();
    for bad in [f64::NAN, f64::INFINITY, 0., -1.] {
        let mut p = m;
        p.beta_amps_per_volt_squared = bad;
        assert!(p.validate().is_err());
        let mut p = m;
        p.surface_potential_volts = bad;
        assert!(p.validate().is_err());
    }
    assert!(m.linearize([0.5, 1.5, 0., 0.1]).is_err());
    assert!(m.linearize([0.5, f64::NAN, 0., -0.2]).is_err());
    let mut no_body = m;
    no_body.body_effect_sqrt_volts = 0.;
    assert!(no_body.linearize([0.5, 1.5, 0., 0.1]).is_ok());
}

proptest::proptest! {
 #![proptest_config(proptest::test_runner::Config { cases:128,rng_seed:proptest::test_runner::RngSeed::Fixed(2026110202),..Default::default() })]
 #[test]
 fn random_reverse_mode_and_polarity_preserve_current_and_terminal_derivatives(d in 0.02f64..3.,g in -0.5f64..4.,body in -2f64..-0.02) {
    let m=model();let v=[d,g,0.,body];let c=m.linearize(v).unwrap();
    let reversed=m.linearize([0.,g,d,body]).unwrap();
    proptest::prop_assert!((reversed.current_amps+c.current_amps).abs()<1e-15);
    let mut p=m;p.p_channel=true;p.threshold_volts = -m.threshold_volts;
    let mirrored=p.linearize(v.map(|x|-x)).unwrap();
    proptest::prop_assert!((mirrored.current_amps+c.current_amps).abs()<1e-15);
    for terminal in 0..4 {
        let mut plus=v;let mut minus=v;plus[terminal]+=1e-6;minus[terminal]-=1e-6;
        let fd=(m.linearize(plus).unwrap().current_amps-m.linearize(minus).unwrap().current_amps)/2e-6;
        proptest::prop_assert!((fd-c.partials_siemens[terminal]).abs()<1e-9);
        proptest::prop_assert!((mirrored.partials_siemens[terminal]-c.partials_siemens[terminal]).abs()<1e-15);
    }
 }
}

fn fixture() -> Request { serde_json::from_value(serde_json::json!({"schema_version":"1","case_id":"mos1-channel","backend":"lu","circuit":{"nodes":["0","d","g","b"],"elements":[{"kind":"voltage","id":"VD","p":1,"n":0,"volts":0.5},{"kind":"voltage","id":"VG","p":2,"n":0,"volts":1.5},{"kind":"voltage","id":"VB","p":3,"n":0,"volts":-0.2},{"kind":"mos1_channel","id":"M1","p":1,"n":0,"gate":2,"bulk":3,"model":{"p_channel":false,"threshold_volts":0.7,"beta_amps_per_volt_squared":0.0002,"body_effect_sqrt_volts":0.4,"surface_potential_volts":0.6,"channel_length_modulation_per_volt":0.03}}]},"analysis":{"kind":"operating_point"}})).unwrap() }
#[test]
fn pure_channel_ac_has_correct_partials_and_explicit_unavailable_bias_audit() {
    let mut request=fixture();
    request.analysis=serde_json::from_value(serde_json::json!({"kind":"ac","frequencies_hz":[1e6],"source_phasors":[{"id":"VG","real":1.,"imag":0.},{"id":"VD","real":0.,"imag":0.},{"id":"VB","real":0.,"imag":0.}]})).unwrap();
    let result=run(&request).unwrap();
    let index=result.signals.iter().position(|s|s.id=="i_channel(M1)").unwrap();
    let expected=model().linearize([0.5,1.5,0.,-0.2]).unwrap().partials_siemens[1];
    assert!((result.ac_samples[0].values[index].real-expected).abs()<1e-12);
    assert!(result.ac_samples[0].values[index].imag.abs()<1e-12);
    assert!(result.consistency.checks.iter().any(|c|c.id=="independent_physical_audit"&&matches!(c.status,circuit_simulator::audit::CheckStatus::NotChecked)));
}#[test]
fn direct_audit_refuses_channel_states_outside_the_declared_body_domain() {
    let request=fixture();
    let state=[0.5,1.5,0.1,0.,0.,0.];
    assert!(circuit_simulator::audit::dc(&request.circuit,&state,Default::default(),None).is_err());
}
