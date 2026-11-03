//! A real charge-only companion must not introduce a hidden conduction current.
use circuit_simulator::api::{Request, run};
fn request(analysis: serde_json::Value) -> Request {
    serde_json::from_value(serde_json::json!({"schema_version":"1","case_id":"storage-only","backend":"lu","circuit":{"nodes":["0","in","out"],"elements":[{"kind":"voltage","id":"V1","p":1,"n":0,"volts":0.6},{"kind":"resistor","id":"R1","p":1,"n":2,"ohms":1000.},{"kind":"junction_storage","id":"QBE","p":2,"n":0,"model":{"saturation_amps":1e-14,"thermal_volts":0.02585,"junction_farads":2e-12,"junction_volts":0.8,"grading":0.5,"forward_coefficient":0.5,"transit_seconds":1e-9}}]},"analysis":analysis})).unwrap()
}
#[test]
fn charge_only_has_zero_dc_conduction() {
    let r = run(&request(serde_json::json!({"kind":"operating_point"}))).unwrap();
    assert!((r.samples[0].values[1] - 0.6).abs() < 1e-12);
    let total = r.signals.iter().position(|s| s.id == "i(QBE)").unwrap();
    assert_eq!(r.samples[0].values[total], 0.);
}
#[test]
fn charge_only_ac_uses_depletion_and_transport_derivative() {
    let result=run(&request(serde_json::json!({"kind":"ac","frequencies_hz":[1e6],"source_phasors":[{"id":"V1","real":1.,"imag":0.}]}))).unwrap();
    let m = circuit_simulator::spice_diode::SpiceDiodeModel {
        saturation_amps: 1e-14,
        thermal_volts: 0.02585,
        junction_farads: 2e-12,
        junction_volts: 0.8,
        grading: 0.5,
        forward_coefficient: 0.5,
        transit_seconds: 1e-9,
    };
    let c = m.charge(0.6).unwrap().capacitance_farads;
    let wrc = std::f64::consts::TAU * 1e6 * 1000. * c;
    let expected_re = 1. / (1. + wrc * wrc);
    let expected_im = -wrc * expected_re;
    let x = &result.ac_samples[0].values[1];
    assert!((x.real - expected_re).abs() < 1e-12);
    assert!((x.imag - expected_im).abs() < 1e-12);
}
#[test]
fn charge_only_be_terminal_current_obeys_kcl() {
    let r=run(&request(serde_json::json!({"kind":"transient","options":{"stop_s":1e-9,"step_s":1e-10,"max_steps":10}}))).unwrap();
    let total = r.signals.iter().position(|s| s.id == "i(QBE)").unwrap();
    for s in &r.samples {
        assert!((s.values[total] - (s.values[0] - s.values[1]) / 1000.).abs() < 1e-10);
    }
}
#[test]
fn individual_depletion_or_transport_terms_have_true_ac_displacement_current() {
    for (cjo, tt) in [(2e-12, 0.), (0., 1e-9), (2e-12, 1e-9)] {
        let mut r = request(
            serde_json::json!({"kind":"ac","frequencies_hz":[1e6],"source_phasors":[{"id":"V1","real":1.,"imag":0.}]}),
        );
        let circuit_simulator::circuit::Element::JunctionStorage { model, .. } =
            &mut r.circuit.elements[2]
        else {
            panic!("storage")
        };
        model.junction_farads = cjo;
        model.transit_seconds = tt;
        let cdep = cjo / (1. - 0.5f64).powf(1.5) * (1. - 0.5 * 1.5 + 0.5 * 0.6 / 0.8);
        let cap = cdep + tt * 1e-14 * (0.6f64 / 0.02585).exp() / 0.02585;
        let wrc = std::f64::consts::TAU * 1e6 * 1000. * cap;
        let real = 1. / (1. + wrc * wrc);
        let imag = -wrc * real;
        let result = run(&r).unwrap();
        let q = result
            .signals
            .iter()
            .position(|s| s.id == "i(QBE)")
            .unwrap();
        let v = &result.ac_samples[0].values[1];
        let current = &result.ac_samples[0].values[q];
        assert!((v.real - real).abs() < 1e-12);
        assert!((v.imag - imag).abs() < 1e-12);
        assert!((current.real - (1. - real) / 1000.).abs() < 1e-15);
        assert!((current.imag + imag / 1000.).abs() < 1e-15);
    }
}
#[test]
fn pure_storage_does_not_pretend_to_provide_a_dc_reference_path() {
    let mut r = request(serde_json::json!({"kind":"operating_point"}));
    r.circuit.elements.retain(|e| {
        matches!(
            e,
            circuit_simulator::circuit::Element::JunctionStorage { .. }
        )
    });
    let warnings = circuit_simulator::topology::inspect(&r.circuit).unwrap();
    assert!(
        warnings
            .iter()
            .any(|d| d.code == "no_obvious_dc_reference_path" && d.subject == "out")
    );
}
