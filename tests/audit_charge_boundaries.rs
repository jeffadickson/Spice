//! Closed-form storage and invalid coupled-domain states, without solver stamps
//! or production charge evaluators as the expected-result oracle.
use circuit_simulator::{audit::{self,AuditOptions,CheckStatus},circuit::{Circuit,Element},
    ids::NodeId as N,spice_diode::{SpiceDiodeModel,EarlyChargeControl}};
use circuit_simulator::{configured::{TransientConfig,Method},transient::Sample,error::ErrorCode};
use proptest::{prelude::*,test_runner::{Config,RngSeed,TestRunner}};

fn model()->SpiceDiodeModel {SpiceDiodeModel {saturation_amps:1e-15,thermal_volts:0.02585,
    junction_farads:3e-6,junction_volts:1.,grading:0.,forward_coefficient:0.5,transit_seconds:0.}}

#[test]
fn small_linear_junction_charge_is_not_lost_by_subtraction_from_one() {
    // With grading=0 and TT=0 the complete storage law is exactly Q=C*V.
    // Tiny voltages challenge cancellation, not model fitting or a solved state.
    for v in [-1e-18,1e-18,1e-17] {
        for h in [1e-15,3e-15] {
            let current=3e-6*v/h;
            let circuit=Circuit{nodes:vec!["0".into(),"a".into()],elements:vec![
                Element::Voltage{id:"V".into(),p:N(1),n:N(0),volts:v},
                Element::JunctionStorage{id:"Q".into(),p:N(1),n:N(0),model:model(),early_effect:None}]};
            let report=audit::backward_euler_step(&circuit,&[0.,0.],&[v,-current],h,AuditOptions::default()).unwrap();
            assert!(!report.has_failures(),"v={v} h={h}: {:?}",report.checks);
            assert_eq!(report.checks.iter().find(|c|c.id=="be_energy_balance").unwrap().status,CheckStatus::NotChecked);
            let wrong=audit::backward_euler_step(&circuit,&[0.,0.],&[v,0.],h,AuditOptions::default()).unwrap();
            assert!(wrong.has_failures(),"lost displacement must be observable");
        }
    }
}

#[test]
fn early_control_domain_is_validated_even_at_zero_or_reverse_be_bias() {
    for be in [0.,-0.1,0.1] {
        for (cp,cn,va) in [(30.,-30.,50.),(f64::MAX,-f64::MAX,50.),(-f64::MAX,f64::MAX,50.),(-1e300,0.,1e-300)] {
            let mut elements:Vec<_>=[be,cp,cn].into_iter().enumerate().map(|(i,volts)|
                Element::Voltage{id:format!("V{i}"),p:N(i+1),n:N(0),volts}).collect();
            elements.push(Element::JunctionStorage{id:"Q".into(),p:N(1),n:N(0),model:model(),
                early_effect:Some(EarlyChargeControl{cp:N(2),cn:N(3),volts:va})});
            let circuit=Circuit{nodes:["0","be","cp","cn"].map(str::to_owned).to_vec(),elements};
            let state=[be,cp,cn,0.,0.,0.];
            assert!(matches!(audit::backward_euler_step(&circuit,&state,&state,1e-6,AuditOptions::default()),
                Err(e) if e.code==ErrorCode::InvalidInput),
                "unsupported BC={cp}-{cn} at BE={be} must return a typed domain error");
            let config=TransientConfig{stop_s:1e-6,max_step_s:1e-6,method:Method::Trapezoidal,..TransientConfig::default()};
            assert!(matches!(audit::configured_step(&circuit,&Sample{time_s:0.,values:state.to_vec()},
                &Sample{time_s:1e-6,values:state.to_vec()},&config,AuditOptions::default()),
                Err(e) if e.code==ErrorCode::InvalidInput));
        }
    }
}

#[test]
fn finite_supported_early_domains_preserve_stationary_conservation() {
    for be in [0.,-0.,-0.1,0.1] {
        for bc in [-30.,0.,20.] {
            let mut elements:Vec<_>=[be,bc+2.,2.].into_iter().enumerate().map(|(i,volts)|
                Element::Voltage{id:format!("V{i}"),p:N(i+1),n:N(0),volts}).collect();
            elements.push(Element::JunctionStorage{id:"Q".into(),p:N(1),n:N(0),model:model(),
                early_effect:Some(EarlyChargeControl{cp:N(2),cn:N(3),volts:50.})});
            let circuit=Circuit{nodes:["0","be","cp","cn"].map(str::to_owned).to_vec(),elements};
            let state=[be,bc+2.,2.,0.,0.,0.];
            let report=audit::backward_euler_step(&circuit,&state,&state,1e-6,AuditOptions::default()).unwrap();
            assert!(!report.has_failures());
            let config=TransientConfig{stop_s:1e-6,max_step_s:1e-6,method:Method::Trapezoidal,..TransientConfig::default()};
            let report=audit::configured_step(&circuit,&Sample{time_s:0.,values:state.to_vec()},
                &Sample{time_s:1e-6,values:state.to_vec()},&config,AuditOptions::default()).unwrap();
            assert!(!report.has_failures());
        }
    }
}

#[test]
fn zero_transport_charge_does_not_overflow_a_discarded_parameter_product() {
    // Finite-domain stress, not realistic device qualification: I(0)=0 and
    // dI/dV=IS/VT=1, so Q=TT*0=0 and dQ/dV=TT remain finite.
    let model=SpiceDiodeModel{saturation_amps:1e200,thermal_volts:1e200,
        transit_seconds:1e200,junction_farads:0.,..model()};
    let circuit=Circuit{nodes:vec!["0".into(),"a".into()],elements:vec![
        Element::Voltage{id:"V".into(),p:N(1),n:N(0),volts:0.},
        Element::JunctionStorage{id:"Q".into(),p:N(1),n:N(0),model,early_effect:None}]};
    let report=audit::backward_euler_step(&circuit,&[0.,0.],&[0.,0.],1.,AuditOptions::default()).unwrap();
    assert!(!report.has_failures());
}

#[test]
fn depletion_transition_uses_declared_fc_not_a_rounded_reconstructed_ratio() {
    // Retained first sample of a seed2026111906 boundary probe. Reconstructing
    // FC as (FC*VJ)/VJ changes one ulp here. Near the depletion singularity that
    // becomes material charge, not merely a different final rounding bit.
    let fc=f64::from_bits(1.0_f64.to_bits()-1);
    let vj=8.106362047980708;
    let v=fc*vj;
    assert_ne!(v/vj,fc);
    let m=SpiceDiodeModel{junction_farads:1e-9,junction_volts:vj,grading:0.75,forward_coefficient:fc,..model()};
    // At FC*VJ the forward polynomial's constant is the depletion integral.
    // For grading3/4, its fractional power is an independent nested square root.
    let q=4.*vj*1e-9*(1.-(1.-fc).sqrt().sqrt());
    let h=1e-6;
    let circuit=Circuit{nodes:vec!["0".into(),"a".into()],elements:vec![
        Element::Voltage{id:"V".into(),p:N(1),n:N(0),volts:v},
        Element::JunctionStorage{id:"Q".into(),p:N(1),n:N(0),model:m,early_effect:None}]};
    let report=audit::backward_euler_step(&circuit,&[0.,0.],&[v,-q/h],h,AuditOptions::default()).unwrap();
    assert!(!report.has_failures(),"{report:?}");
}

#[test]
fn small_charge_property_uses_a_closed_form_capacitor_oracle() {
    let config=Config{cases:96,rng_seed:RngSeed::Fixed(2026111906),failure_persistence:None,..Config::default()};
    TestRunner::new(config).run(&(1e-6..4e-6,1e-18..1e-17,1e-15..3e-15,any::<bool>()),
        |(farads,magnitude,h,negative):(f64,f64,f64,bool)| {
        let v=if negative{-magnitude}else{magnitude};let current=farads*v/h;
        let circuit=Circuit{nodes:vec!["0".into(),"a".into()],elements:vec![
            Element::Voltage{id:"V".into(),p:N(1),n:N(0),volts:v},
            Element::JunctionStorage{id:"Q".into(),p:N(1),n:N(0),model:SpiceDiodeModel{junction_farads:farads,..model()},early_effect:None}]};
        let report=audit::backward_euler_step(&circuit,&[0.,0.],&[v,-current],h,AuditOptions::default()).unwrap();
        prop_assert!(!report.has_failures(),"{report:?}");
        Ok(())
    }).unwrap();
}
