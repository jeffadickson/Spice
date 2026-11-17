//! One AC linearization owns one verified bias. Counting the supplied provider
//! detects accidental operating-point work in derived-observable loops.
use circuit_simulator::{
    api::{self, Analysis, Request}, backend::DenseLu, compile::compile, dc,
    linalg::{LinearSolver, Matrix}, registry::BackendRegistry,
};
use num_complex::Complex64;
use std::sync::{Arc, atomic::{AtomicUsize, Ordering}};

#[derive(Default)]
struct CountingLu { real:AtomicUsize, complex:AtomicUsize }
impl LinearSolver for CountingLu {
    fn name(&self)->&'static str { "counting_lu" }
    fn solve(&self,a:&Matrix,b:&[f64])->circuit_simulator::error::SimResult<Vec<f64>> {
        self.real.fetch_add(1,Ordering::SeqCst);DenseLu.solve(a,b)
    }
    fn solve_complex_rhs(&self,a:&Matrix,b:&Matrix,rhs:&[Complex64])->circuit_simulator::error::SimResult<Vec<Complex64>> {
        self.complex.fetch_add(1,Ordering::SeqCst);DenseLu.solve_complex_rhs(a,b,rhs)
    }
}
fn request(bias:f64)->Request {
    serde_json::from_value(serde_json::json!({
        "schema_version":"1","case_id":"one-ac-bias","backend":"counting",
        "circuit":{"nodes":["0","out"],"elements":[
            {"kind":"voltage","id":"V","p":1,"n":0,"volts":bias},
            {"kind":"diode","id":"D","p":1,"n":0,"saturation_amps":1e-12,"thermal_volts":0.02585},
            {"kind":"spice_diode","id":"DQ","p":1,"n":0,"model":{
                "saturation_amps":2e-12,"thermal_volts":0.02585,"junction_farads":2e-12,
                "junction_volts":0.8,"grading":0.,"forward_coefficient":0.5,"transit_seconds":1e-9}},
            {"kind":"mos1_channel","id":"M","p":1,"n":0,"gate":1,"bulk":0,"model":{
                "p_channel":false,"threshold_volts":0.05,"beta_amps_per_volt_squared":1e-3,
                "body_effect_sqrt_volts":0.,"surface_potential_volts":0.6,
                "channel_length_modulation_per_volt":0.02}}
        ]},"analysis":{"kind":"ac","frequencies_hz":[1000.,10000.],
            "source_phasors":[{"id":"V","real":0.001,"imag":0.0002}]}
    })).unwrap()
}
fn run(request:&Request)->(api::Manifest,Arc<CountingLu>) {
    let solver=Arc::new(CountingLu::default());let mut registry=BackendRegistry::default();
    registry.register("counting",solver.clone()).unwrap();
    (api::run_with_registry(request,&registry).unwrap(),solver)
}
fn assert_currents(manifest:&api::Manifest,bias:f64) {
    let phasor=Complex64::new(0.001,0.0002);
    let diode_g=1e-12/0.02585*(bias/0.02585).exp();
    let charge_g=2.*diode_g;
    let mos_g=1e-3*(bias-0.05)*(1.+0.02*bias)+0.5e-3*(bias-0.05).powi(2)*0.02;
    for sample in &manifest.ac_samples {
        let charge_c=2e-12+1e-9*charge_g;
        for (id,expected) in [
            ("i(D)",diode_g*phasor),
            ("i_conduction(DQ)",charge_g*phasor),
            ("i(DQ)",Complex64::new(charge_g,std::f64::consts::TAU*sample.frequency_hz*charge_c)*phasor),
            ("i_channel(M)",mos_g*phasor),
        ] {
            let index=manifest.signals.iter().position(|s|s.id==id).unwrap();
            let actual=&sample.values[index];
            assert!((Complex64::new(actual.real,actual.imag)-expected).norm()<=1e-15+1e-9*expected.norm(),"{id}: {actual:?} vs {expected:?}");
        }
    }
}
#[test]
fn all_ac_currents_reuse_exactly_one_bias_and_keep_independent_derivatives() {
    for bias in [0.1,0.15] {
        let request=request(bias);let reference=CountingLu::default();
        dc::operating_point(&compile(&request.circuit).unwrap(),&reference).unwrap();
        let (manifest,solver)=run(&request);
        assert_currents(&manifest,bias);
        assert_eq!(manifest.ac_samples.len(),2);
        assert_eq!(solver.complex.load(Ordering::SeqCst),2);
        assert_eq!(solver.real.load(Ordering::SeqCst),reference.real.load(Ordering::SeqCst),"Derived currents must not trigger additional bias solves");
    }
}
#[test]
fn invalid_ac_grid_is_refused_before_any_provider_work() {
    let mut request=request(0.1);
    if let Analysis::Ac{frequencies_hz,..}=&mut request.analysis { frequencies_hz.push(f64::NAN); }
    let solver=Arc::new(CountingLu::default());let mut registry=BackendRegistry::default();
    registry.register("counting",solver.clone()).unwrap();
    assert!(api::run_with_registry(&request,&registry).is_err());
    assert_eq!(solver.real.load(Ordering::SeqCst),0);assert_eq!(solver.complex.load(Ordering::SeqCst),0);
}

#[test]
fn linear_ac_does_not_add_an_unnecessary_bias_solve() {
    let mut request=request(0.1);
    request.circuit.elements.retain(|e| matches!(e,circuit_simulator::circuit::Element::Voltage{..}));
    let (manifest,solver)=run(&request);
    assert_eq!(manifest.ac_samples.len(),2);
    assert_eq!(solver.real.load(Ordering::SeqCst),0);
    assert_eq!(solver.complex.load(Ordering::SeqCst),2);
}

proptest::proptest! {
    #![proptest_config(proptest::test_runner::Config::with_cases(32))]
    #[test]
    fn request_local_bias_currents_follow_analytical_slopes(bias in 0.06_f64..0.2) {
        let (manifest,_)=run(&request(bias));
        assert_currents(&manifest,bias);
    }
}
