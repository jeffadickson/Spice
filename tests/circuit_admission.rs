//! Exercise the public Rust boundary directly: JSON rejects NaN before these
//! circuit/model checks, and is not a substitute for their negative contracts.
use circuit_simulator::{circuit::{Circuit,Element},error::ErrorCode,ids::NodeId as N,
    bsim330::Model,mos1::Mos1Model,spice_diode::{SpiceDiodeModel,EarlyChargeControl}};
use proptest::{prelude::*,test_runner::{Config,RngSeed,TestRunner}};

fn circuit(element:Element)->Circuit {Circuit{nodes:vec!["0".into(),"a".into()],elements:vec![element]}}
fn rejected(element:Element) {assert_eq!(circuit(element).validate().unwrap_err().code,ErrorCode::InvalidInput);}
fn junction()->SpiceDiodeModel {SpiceDiodeModel{saturation_amps:1e-15,thermal_volts:0.02585,
    junction_farads:1e-12,junction_volts:1.,grading:0.5,forward_coefficient:0.5,transit_seconds:0.}}
fn bulk()->Model {Model{polarity:1,tox:15e-9,nch:1.7e17,nsub:6e16,vth0:0.7,w:1e-5,l:1e-6,
    pd:0.,ps:0.,ad:0.,source_area:0.}}
fn mos()->Mos1Model {Mos1Model{p_channel:false,oxide_farads:0.,threshold_volts:0.7,
    beta_amps_per_volt_squared:2e-4,body_effect_sqrt_volts:0.,surface_potential_volts:0.6,
    channel_length_modulation_per_volt:0.1}}

#[test]
fn passive_values_are_independently_positive_and_finite() {
    for value in [0.,-1.,f64::NAN,f64::INFINITY,f64::NEG_INFINITY] {
        rejected(Element::Capacitor{id:"C".into(),p:N(1),n:N(0),farads:value});
        rejected(Element::Inductor{id:"L".into(),p:N(1),n:N(0),henries:value});
    }
    let config=Config{cases:64,rng_seed:RngSeed::Fixed(2026111915),failure_persistence:None,..Config::default()};
    TestRunner::new(config).run(&(-250_i32..250),|exponent| {
        let value=10_f64.powi(exponent);
        prop_assert!(circuit(Element::Capacitor{id:"C".into(),p:N(1),n:N(0),farads:value}).validate().is_ok(),"positive finite capacitance rejected");
        prop_assert!(circuit(Element::Inductor{id:"L".into(),p:N(1),n:N(0),henries:value}).validate().is_ok(),"positive finite inductance rejected");
        Ok(())
    }).unwrap();
}

#[test]
fn source_values_require_finiteness_but_allow_zero_and_negative_values() {
    for value in [f64::NAN,f64::INFINITY,f64::NEG_INFINITY] {
        rejected(Element::Voltage{id:"V".into(),p:N(1),n:N(0),volts:value});
        rejected(Element::Current{id:"I".into(),p:N(1),n:N(0),amps:value});
    }
    for value in [-1.,0.,1.] {
        circuit(Element::Voltage{id:"V".into(),p:N(1),n:N(0),volts:value}).validate().unwrap();
        circuit(Element::Current{id:"I".into(),p:N(1),n:N(0),amps:value}).validate().unwrap();
    }
}

#[test]
fn each_early_control_operand_is_validated_independently() {
    let valid=EarlyChargeControl{cp:N(1),cn:N(0),volts:50.};
    let element=|control|Element::JunctionStorage{id:"Q".into(),p:N(1),n:N(0),model:junction(),early_effect:Some(control)};
    circuit(element(valid)).validate().unwrap();
    for bad in [EarlyChargeControl{cp:N(2),..valid},EarlyChargeControl{cn:N(2),..valid},
        EarlyChargeControl{volts:f64::NAN,..valid},EarlyChargeControl{volts:f64::INFINITY,..valid},
        EarlyChargeControl{volts:0.,..valid},EarlyChargeControl{volts:-1.,..valid},
        EarlyChargeControl{volts:f64::from_bits(1),..valid}] {rejected(element(bad));}
}

#[test]
fn model_gate_and_body_ports_are_checked_without_loading_a_provider() {
    for (gate,body) in [(N(1),N(0)),(N(2),N(0)),(N(1),N(2))] {
        let elements=[
            Element::BsimSoiFdReference{id:"M".into(),p:N(1),n:N(0),gate,back_gate:body},
            Element::Bsim330{id:"M".into(),p:N(1),n:N(0),gate,bulk:body,model:bulk()},
            Element::Mos1Channel{id:"M".into(),p:N(1),n:N(0),gate,bulk:body,model:mos()},
        ];
        for element in elements {if gate.0<2 && body.0<2 {circuit(element).validate().unwrap();}else{rejected(element);}}
    }
    let invalid=Mos1Model{beta_amps_per_volt_squared:f64::NAN,..mos()};
    rejected(Element::Mos1Channel{id:"M".into(),p:N(1),n:N(0),gate:N(1),bulk:N(0),model:invalid});
}

#[test]
fn both_junction_element_variants_validate_the_model() {
    for model in [junction(),SpiceDiodeModel{thermal_volts:0.,..junction()}] {
        for element in [Element::SpiceDiode{id:"D".into(),p:N(1),n:N(0),model},
            Element::JunctionStorage{id:"Q".into(),p:N(1),n:N(0),model,early_effect:None}] {
            if model.thermal_volts>0. {circuit(element).validate().unwrap();}else{rejected(element);}
        }
    }
}

#[test]
fn nonunit_factor_offsets_survive_the_wire_roundtrip() {
    for offset in [-2.,0.,0.5,1.,2.] {
        let element=Element::VoltageScaledCccs{id:"F".into(),p:N(1),n:N(0),cp:N(1),cn:N(0),
            control_id:"V".into(),gain:2.,voltage_coefficient:3.,factor_offset:offset};
        let wire=serde_json::to_value(&element).unwrap();
        assert_eq!(wire.get("factor_offset").is_none(),offset==1.);
        let decoded:Element=serde_json::from_value(wire).unwrap();
        match decoded {Element::VoltageScaledCccs{factor_offset,..}=>assert_eq!(factor_offset,offset),_=>panic!("changed element kind")}
    }
}
