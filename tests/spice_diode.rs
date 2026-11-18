//! Independent junction integrals and derivative checks for the native SPICE profile.
use circuit_simulator::spice_diode::SpiceDiodeModel;
fn model() -> SpiceDiodeModel {
    SpiceDiodeModel {
        saturation_amps: 1e-14,
        thermal_volts: 0.02585,
        junction_farads: 2e-12,
        junction_volts: 0.8,
        grading: 0.5,
        forward_coefficient: 0.5,
        transit_seconds: 1e-9,
    }
}

// Independently forced terminal voltages expose common-mode mistakes that a
// grounded cathode cannot. Zero grading gives the closed-form q=C*v+TT*I(v).
fn floating_request(common: f64, differential: f64, backend: &str, analysis: serde_json::Value)
    -> circuit_simulator::api::Request
{
    serde_json::from_value(serde_json::json!({
        "schema_version":"1","case_id":"floating-junction","backend":backend,
        "circuit":{"nodes":["0","p","n"],"elements":[
            {"kind":"voltage","id":"VP","p":1,"n":0,"volts":common+differential},
            {"kind":"voltage","id":"VN","p":2,"n":0,"volts":common},
            {"kind":"spice_diode","id":"D1","p":1,"n":2,
             "model":{"saturation_amps":1e-14,"thermal_volts":0.02585,
                "junction_farads":2e-12,"junction_volts":0.8,"grading":0.,
                "forward_coefficient":0.5,"transit_seconds":1e-9}}
        ]},"analysis":analysis
    })).unwrap()
}

fn floating_current(result: &circuit_simulator::api::Manifest, id: &str) -> usize {
    result.signals.iter().position(|s| s.id==id).expect("explicit diode signal")
}

#[test]
fn floating_diode_dc_uses_signed_differential_bias_on_both_cpu_providers() {
    for backend in ["lu","faer"] {
        for common in [-0.4,0.2,1.] {
            for differential in [-0.1_f64,0.2,0.5] {
                let result=circuit_simulator::api::run(&floating_request(common,differential,
                    backend,serde_json::json!({"kind":"operating_point"}))).unwrap();
                let expected=1e-14*(differential/0.02585).exp_m1();
                for id in ["i(D1)","i_conduction(D1)"] {
                    let actual=result.samples[0].values[floating_current(&result,id)];
                    assert!((actual-expected).abs()<=1e-18+expected.abs()*1e-9,
                        "{backend},common={common},dv={differential},{id}: {actual} vs{expected}");
                }
            }
        }
    }
}

#[test]
fn floating_diode_backward_euler_current_has_independent_charge_history() {
    let differential=0.5_f64;
    let conduction=1e-14*(differential/0.02585).exp_m1();
    let charge=2e-12*differential+1e-9*conduction;
    for backend in ["lu","faer"] {
        for common in [-0.4,0.2,1.] {
            let result=circuit_simulator::api::run(&floating_request(common,differential,
                backend,serde_json::json!({"kind":"transient","options":{
                    "stop_s":4e-8,"step_s":1e-8,"max_steps":4}}))).unwrap();
            assert_eq!(result.samples.len(),4);
            let total=floating_current(&result,"i(D1)");
            let dc=floating_current(&result,"i_conduction(D1)");
            for (index,sample) in result.samples.iter().enumerate() {
                // Raw BE starts with zero differential charge, then the ideal
                // sources force a fixed bias. Only the first step charges it.
                let expected=conduction+if index==0 {charge/sample.time_s} else {0.};
                assert!((sample.values[total]-expected).abs()<1e-13+expected.abs()*1e-9,
                    "{backend},common={common},step={index}: {} vs{expected}",sample.values[total]);
                assert!((sample.values[dc]-conduction).abs()<1e-15);
            }
        }
    }
}

#[test]
fn floating_diode_ac_current_uses_differential_bias_and_complex_excitation() {
    let dv=0.5_f64;
    let g=1e-14*(dv/0.02585).exp()/0.02585;
    let capacitance=2e-12+1e-9*g;
    for backend in ["lu","faer"] {
        for common in [-0.4,0.2,1.] {
            let result=circuit_simulator::api::run(&floating_request(common,dv,backend,
                serde_json::json!({"kind":"ac","frequencies_hz":[1e3,1e6],
                 "source_phasors":[{"id":"VP","real":0.7,"imag":0.3},
                                   {"id":"VN","real":0.2,"imag":-0.1}]}))).unwrap();
            let total=floating_current(&result,"i(D1)");
            let conduction=floating_current(&result,"i_conduction(D1)");
            assert_eq!(result.ac_samples.len(),2);
            for (sample,frequency) in result.ac_samples.iter().zip([1e3,1e6]) {
                assert_eq!(sample.frequency_hz,frequency);
                let wc=std::f64::consts::TAU*sample.frequency_hz*capacitance;
                let actual=&sample.values[total];
                assert!((actual.real-(0.5*g-0.4*wc)).abs()<1e-13);
                assert!((actual.imag-(0.4*g+0.5*wc)).abs()<1e-13);
                assert!((sample.values[conduction].real-0.5*g).abs()<1e-13);
                assert!((sample.values[conduction].imag-0.4*g).abs()<1e-13);
            }
        }
    }
}

proptest::proptest! {
    #![proptest_config(proptest::test_runner::Config {cases:64,
        rng_seed:proptest::test_runner::RngSeed::Fixed(2026111801),..Default::default()})]
    #[test]
    fn diode_dc_current_is_invariant_under_generated_common_mode(
        common in -1.0f64..1., differential in 0.1f64..0.5) {
        let reference=1e-14*(differential/0.02585).exp_m1();
        let result=circuit_simulator::api::run(&floating_request(common,differential,"lu",
            serde_json::json!({"kind":"operating_point"}))).unwrap();
        let actual=result.samples[0].values[floating_current(&result,"i(D1)")];
        proptest::prop_assert!((actual-reference).abs()<1e-18+reference.abs()*1e-9);
    }
}
#[test]
fn depletion_is_voltage_dependent_and_matches_integral() {
    let m = model();
    for v in [-2., -0.1, 0., 0.3] {
        let q = m.charge(v).unwrap();
        let i = m.linearize(v).unwrap();
        let expected =
            2. * m.junction_farads * m.junction_volts * (1. - (1. - v / m.junction_volts).sqrt())
                + m.transit_seconds * i.current_amps;
        assert!((q.charge_coulombs - expected).abs() < 1e-25);
    }
}
#[test]
fn derivative_and_transition_are_consistent() {
    let m = model();
    for v in [-1., 0., 0.399, 0.4, 0.401, 0.6] {
        let h = 1e-7;
        let q = m.charge(v).unwrap();
        let fd = (m.charge(v + h).unwrap().charge_coulombs
            - m.charge(v - h).unwrap().charge_coulombs)
            / (2. * h);
        assert!((fd - q.capacitance_farads).abs() / q.capacitance_farads < 1e-7);
    }
}
#[test]
fn rejects_nonphysical_domains_and_nonfinite_trials() {
    for which in 0..7 {
        let mut m = model();
        match which {
            0 => m.saturation_amps = 0.,
            1 => m.thermal_volts = 0.,
            2 => m.junction_farads = -1.,
            3 => m.junction_volts = 0.,
            4 => m.grading = 1.,
            5 => m.forward_coefficient = 1.,
            _ => m.transit_seconds = -1.,
        };
        assert!(m.validate().is_err());
    }
    assert!(model().charge(f64::NAN).is_err());
}
#[test]
fn serialized_profile_runs_all_three_analyses() {
    for analysis in [
        serde_json::json!({"kind":"operating_point"}),
        serde_json::json!({"kind":"ac","frequencies_hz":[1e3]}),
        serde_json::json!({"kind":"transient","options":{"stop_s":1e-6,"step_s":1e-7,"max_steps":10}}),
    ] {
        let r: circuit_simulator::api::Request=serde_json::from_value(serde_json::json!({"schema_version":"1","case_id":"junction","circuit":{"nodes":["0","in","out"],"elements":[{"kind":"voltage","id":"V1","p":1,"n":0,"volts":0.5},{"kind":"resistor","id":"R1","p":1,"n":2,"ohms":1000.},{"kind":"spice_diode","id":"D1","p":2,"n":0,"model":model()}]},"analysis":analysis,"backend":"lu"})).unwrap();
        circuit_simulator::api::run(&r).unwrap();
    }
}

#[test]
fn total_current_includes_displacement_and_conduction_remains_explicit() {
    let json = serde_json::json!({"schema_version":"1","case_id":"total","backend":"lu","circuit":{"nodes":["0","in","out"],"elements":[{"kind":"voltage","id":"V1","p":1,"n":0,"volts":0.5},{"kind":"resistor","id":"R1","p":1,"n":2,"ohms":1000.},{"kind":"spice_diode","id":"D1","p":2,"n":0,"model":model()}]},"analysis":{"kind":"transient","options":{"stop_s":1e-9,"step_s":1e-10,"max_steps":10}}});
    let request = serde_json::from_value(json).unwrap();
    let result = circuit_simulator::api::run(&request).unwrap();
    let total = result
        .signals
        .iter()
        .position(|s| s.id == "i(D1)")
        .expect("total device current");
    let cond = result
        .signals
        .iter()
        .position(|s| s.id == "i_conduction(D1)")
        .unwrap();
    for sample in &result.samples {
        let expected = (sample.values[0] - sample.values[1]) / 1000.;
        assert!((sample.values[total] - expected).abs() < 1e-10);
        assert!(sample.values[total] > sample.values[cond]);
    }
    assert!(
        result
            .consistency
            .checks
            .iter()
            .any(|c| c.id == "be_energy_balance"
                && c.status == circuit_simulator::audit::CheckStatus::NotChecked)
    );
    assert!(
        result
            .consistency
            .checks
            .iter()
            .filter(|c| c.id.starts_with("kcl:"))
            .all(|c| c.status == circuit_simulator::audit::CheckStatus::Passed)
    );
}
proptest::proptest! {
 #![proptest_config(proptest::test_runner::Config { cases:128,rng_seed:proptest::test_runner::RngSeed::Fixed(2026110201),..Default::default() })]
 #[test] fn randomized_charge_derivative(v in -4.0f64..0.65,m in 0.0f64..0.95,fc in 0.1f64..0.8,c in 1e-14f64..1e-10,tt in 0.0f64..1e-7) {
  let mut d=model();d.grading=m;d.forward_coefficient=fc;d.junction_farads=c;d.transit_seconds=tt;
  let h=1e-7;let q=d.charge(v).unwrap();let derivative=(d.charge(v+h).unwrap().charge_coulombs-d.charge(v-h).unwrap().charge_coulombs)/(2.*h);
  proptest::prop_assert!(q.capacitance_farads>0.);proptest::prop_assert!((derivative-q.capacitance_farads).abs()/q.capacitance_farads<2e-7);
 }
}
#[test]
fn unknown_wire_parameters_and_unqualified_history_policies_refuse() {
    for element in [
        serde_json::json!({"kind":"spice_diode","id":"D1","p":1,"n":0,"model":{"saturation_amps":1e-14,"thermal_volts":0.02585,"junction_farads":2e-12,"junction_volts":0.8,"grading":0.5,"forward_coefficient":0.5,"transit_seconds":1e-9,"rs":1.}}),
    ] {
        assert!(serde_json::from_value::<circuit_simulator::circuit::Element>(element).is_err());
    }
    let json = serde_json::json!({"schema_version":"1","case_id":"invalid-history","backend":"lu","circuit":{"nodes":["0","in"],"elements":[{"kind":"voltage","id":"V1","p":1,"n":0,"volts":0.5},{"kind":"spice_diode","id":"D1","p":1,"n":0,"model":model()}]},"analysis":{"kind":"transient_configured","options":{"stop_s":1e-9,"max_step_s":1e-10,"max_steps":10,"initial_state":[0.5,0.]}}});
    let request = serde_json::from_value(json).unwrap();
    assert_eq!(
        circuit_simulator::api::run(&request).unwrap_err().code,
        circuit_simulator::error::ErrorCode::Unsupported
    );
}

#[test]
fn complex_terminal_current_balances_source_at_each_frequency() {
    for backend in ["lu", "faer"] {
        let request = serde_json::from_value(serde_json::json!({
            "schema_version": "1", "case_id": "ac-terminal-kcl", "backend": backend,
            "circuit": {"nodes": ["0", "out"], "elements": [
                {"kind": "voltage", "id": "V1", "p": 1, "n": 0, "volts": 0.3},
                {"kind": "spice_diode", "id": "D1", "p": 1, "n": 0, "model": model()}
            ]},
            "analysis": {"kind": "ac", "frequencies_hz": [1e3, 1e6],
                "source_phasors": [{"id": "V1", "real": 0.75, "imag": 0.25}]}
        }))
        .unwrap();
        let result = circuit_simulator::api::run(&request).unwrap();
        let column = |id| result.signals.iter().position(|s| s.id == id).unwrap();
        let source = column("i(V1)");
        let diode = column("i(D1)");
        for sample in &result.ac_samples {
            let a = &sample.values[source];
            let b = &sample.values[diode];
            assert!((a.real + b.real).abs() < 1e-12);
            assert!((a.imag + b.imag).abs() < 1e-12);
        }
    }
}

#[test]
fn floating_diode_ac_current_uses_signed_terminal_phasor_difference() {
    use circuit_simulator::api::{Request, run};
    use num_complex::Complex64;

    // Equal DC terminals give an independently known slope IS/VT.
    // Unequal, nonzero imaginary terminal drives distinguish subtraction
    // from addition in both parts of the published junction current.
    let junction = model();
    let g = junction.saturation_amps / junction.thermal_volts;
    let frequency = 1000.;
    let c = junction.junction_farads + junction.transit_seconds * g;
    let expected =
        Complex64::new(1.5, -0.75) * Complex64::new(g, std::f64::consts::TAU * frequency * c);
    for backend in ["lu", "faer"] {
        let request: Request = serde_json::from_value(serde_json::json!({
            "schema_version":"1","case_id":"floating-diode-ac","backend":backend,
            "circuit":{"nodes":["0","p","n"],"elements":[
                {"kind":"voltage","id":"Vp","p":1,"n":0,"volts":0.1},
                {"kind":"voltage","id":"Vn","p":2,"n":0,"volts":0.1},
                {"kind":"spice_diode","id":"D1","p":1,"n":2,"model":junction}
            ]},
            "analysis":{"kind":"ac","frequencies_hz":[frequency],"source_phasors":[
                {"id":"Vp","real":2.,"imag":0.25},
                {"id":"Vn","real":0.5,"imag":1.}
            ]}
        }))
        .unwrap();
        let result = run(&request).unwrap();
        let index = |id: &str| result.signals.iter().position(|s| s.id == id).unwrap();
        let values = &result.ac_samples[0].values;
        let current = |id| {
            let x = &values[index(id)];
            Complex64::new(x.real, x.imag)
        };
        assert!((current("i(D1)") - expected).norm() < 1e-16, "{backend}");
        assert!((current("i(Vp)") + expected).norm() < 1e-16, "{backend}");
        assert!((current("i(Vn)") - expected).norm() < 1e-16, "{backend}");
    }
}
