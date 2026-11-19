//! Analytic memoryless MOS current and independently invalid differential states.
use circuit_simulator::{audit::{self,AuditOptions},circuit::{Circuit,Element},
    ids::NodeId as N,mos1::Mos1Model,error::ErrorCode};
use proptest::{prelude::*,test_runner::{Config,RngSeed,TestRunner}};

fn model(p_channel:bool)->Mos1Model {Mos1Model{p_channel,oxide_farads:0.,
    threshold_volts:if p_channel{-0.7}else{0.7},beta_amps_per_volt_squared:2e-4,
    body_effect_sqrt_volts:0.,surface_potential_volts:0.6,channel_length_modulation_per_volt:0.1}}

fn mos(p_channel:bool)->Element {Element::Mos1Channel{id:"M".into(),p:N(1),gate:N(2),n:N(3),bulk:N(4),model:model(p_channel)}}

fn known_state(common:f64,p_channel:bool,reverse:bool)->(Circuit,Vec<f64>) {
    let polarity=if p_channel{-1.}else{1.};
    let normalized=if reverse{[0.,1.5,0.5,0.]}else{[0.5,1.5,0.,0.]};
    let volts=normalized.map(|v|common+polarity*v);
    // Triode law: beta*(Vov*Vds-Vds^2/2)*(1+lambda*Vds),
    // Vov=.8,Vds=.5 ->57.75 microamps. Polarity and swapping D/S
    // change only its terminal sign, without a production evaluator oracle.
    let drain=polarity*if reverse{-57.75e-6}else{57.75e-6};
    let mut elements:Vec<_>=volts.into_iter().enumerate().map(|(i,volts)|
        Element::Voltage{id:format!("V{i}"),p:N(i+1),n:N(0),volts}).collect();
    elements.push(mos(p_channel));
    let circuit=Circuit{nodes:["0","d","g","s","b"].map(str::to_owned).to_vec(),elements};
    let mut state=volts.to_vec();state.extend([-drain,0.,drain,0.]);(circuit,state)
}

fn verify_known_state(common:f64,p_channel:bool,reverse:bool) {
    let (circuit,state)=known_state(common,p_channel,reverse);
    let report=audit::dc(&circuit,&state,AuditOptions::default(),None).unwrap();
    assert!(!report.has_failures(),"common={common} p={p_channel} reverse={reverse}: {report:?}");
    assert_eq!(report.checks.iter().filter(|c|c.id.starts_with("kcl:")).count(),5);
    for h in [1e-6,2e-6] {
        let report=audit::backward_euler_step(&circuit,&state,&state,h,AuditOptions::default()).unwrap();
        assert!(!report.has_failures());
    }
    let mut wrong=state;wrong[4]+=1e-5;
    assert!(audit::dc(&circuit,&wrong,AuditOptions::default(),None).unwrap().has_failures());
}

#[test]
fn nmos_pmos_and_reversed_terminals_agree_with_known_triode_current() {
    for common in [-1.,0.,2.] {for p in [false,true] {for reverse in [false,true] {
        verify_known_state(common,p,reverse);
    }}}
}

fn invalid_differential(normalized:[f64;4]) {
    for p_channel in [false,true] {
        let polarity=if p_channel{-1.}else{1.};
        // No ideal clamps: their own huge-voltage bounds must not be an
        // incidental reason for rejecting an otherwise silent zero-current audit.
        let circuit=Circuit{nodes:["0","d","g","s","b"].map(str::to_owned).to_vec(),elements:vec![mos(p_channel)]};
        let state=normalized.map(|v|polarity*v);
        let dc=audit::dc(&circuit,&state,AuditOptions::default(),None);
        assert!(matches!(&dc,Err(e) if e.code==ErrorCode::InvalidInput),"{dc:?}");
        let step=audit::backward_euler_step(&circuit,&state,&state,1e-6,AuditOptions::default());
        assert!(matches!(&step,Err(e) if e.code==ErrorCode::InvalidInput),"{step:?}");
    }
}

#[test]
fn body_overflow_is_refused_even_with_finite_zero_channel_span() {
    invalid_differential([-f64::MAX,-f64::MAX,-f64::MAX,f64::MAX]);
}

#[test]
fn channel_span_overflow_is_refused_even_with_finite_body_bias() {
    invalid_differential([f64::MAX,-f64::MAX,-f64::MAX,-f64::MAX]);
}

#[test]
fn gate_overdrive_overflow_is_refused_with_finite_threshold_span_and_body() {
    // Threshold remains +/-0.7 V, span and body are zero. Only gate-minus-
    // effective-source overflows. Negative infinity must not become cutoff;
    // positive infinity must not become a later generic numerical failure.
    invalid_differential([f64::MAX,-f64::MAX,f64::MAX,f64::MAX]);
    invalid_differential([-f64::MAX,f64::MAX,-f64::MAX,-f64::MAX]);
}

#[test]
fn current_conservation_survives_common_mode_polarity_and_orientation() {
    let config=Config{cases:96,rng_seed:RngSeed::Fixed(2026111908),failure_persistence:None,..Config::default()};
    TestRunner::new(config).run(&(-10_f64..10.,any::<bool>(),any::<bool>()),|(common,p,reverse)| {
        verify_known_state(common,p,reverse);Ok(())
    }).unwrap();
}

#[test]
fn zero_body_bias_has_no_shift_with_nonzero_body_effect_coefficients() {
    for gamma in [0.4,2.] {for p in [false,true] {for reverse in [false,true] {
        let (mut circuit,state)=known_state(0.,p,reverse);
        if let Element::Mos1Channel{model,..}=circuit.elements.last_mut().unwrap() {
            model.body_effect_sqrt_volts=gamma;
        } else {panic!("expected MOS fixture")}
        // At Vbs=0, sqrt(phi-Vbs)-sqrt(phi)=0 for every gamma.
        assert!(!audit::dc(&circuit,&state,AuditOptions::default(),None).unwrap().has_failures());
        assert!(!audit::backward_euler_step(&circuit,&state,&state,1e-6,AuditOptions::default()).unwrap().has_failures());
    }}}
}
