//! November18 backlog regressions: independent physical checks and exact input boundaries.
use circuit_simulator::{
    audit::{self, AuditOptions}, backend::DenseLu, circuit::{Circuit,Element}, compile::compile,
    configured::{Method,TransientConfig}, experiment::{AssertionSpec,ExperimentSpec,MeasurementSpec,Operation},
    expression::Expr, ids::NodeId as N, netlist::{parse,import_ngspice_request},
    newton::{self,NewtonOptions}, sources::SourceOverride, transient::Sample,
    variation::{self,ScalarGrid,ScalarSweep}, waveform::{Point,Waveform},
};
fn resistor(id: String)->Circuit {
    Circuit{nodes:vec!["0".into(),"a".into()],elements:vec![Element::Resistor{id,p:N(1),n:N(0),ohms:2.}]}
}
fn experiment()->ExperimentSpec {
    ExperimentSpec{measurements:vec![MeasurementSpec{id:"m".into(),signal:"v(a)".into(),operation:Operation::ValueAt{time_s:0.}}],assertions:vec![AssertionSpec{id:"a".into(),measurement:"m".into(),unit:"V".into(),low:0.,high:1.}]}
}
#[test]
fn exact_identifier_byte_limits_are_accepted_and_next_byte_is_rejected() {
    // Byte counts are the wire contract; also check multibyte names at the limit.
    for id in ["r".repeat(256),"é".repeat(128)] {
        let c=resistor(id.clone());c.validate().unwrap();
        let changed=variation::replace_scalar(&c,&id,4.).unwrap();
        assert!(matches!(changed.elements[0],Element::Resistor{ohms:4.,..}));
    }
    assert!(resistor("r".repeat(257)).validate().is_err());
    let mut spec=experiment();spec.measurements[0].id="m".repeat(256);
    spec.assertions[0].measurement=spec.measurements[0].id.clone();spec.assertions[0].id="a".repeat(256);
    spec.validate().unwrap();
    let mut bad=spec.clone();bad.measurements[0].id.push('m');assert!(bad.validate().is_err());
    spec.assertions[0].id.push('a');assert!(spec.validate().is_err());
    assert!(circuit_simulator::sweep::derived_case_id(&"b".repeat(220),'s',63).is_ok());
    assert!(circuit_simulator::sweep::derived_case_id(&"b".repeat(221),'s',0).is_err());
    for len in [1,63,64]{assert!(circuit_simulator::provenance::typed_hash(&"d".repeat(len),&1).is_ok());}
    for len in [0,65,100]{assert!(circuit_simulator::provenance::typed_hash(&"d".repeat(len),&1).is_err());}
}
#[test]
fn each_measurement_window_endpoint_must_be_finite_independently() {
    for operation in [0,1,2,3] { for (start_s,stop_s) in [(f64::NAN,1.),(0.,f64::NAN),(f64::INFINITY,1.),(0.,f64::INFINITY)] {
        let mut spec=experiment();spec.measurements[0].operation=match operation {0=>Operation::Minimum{start_s,stop_s},1=>Operation::Maximum{start_s,stop_s},2=>Operation::Integral{start_s,stop_s},_=>Operation::Rms{start_s,stop_s}};
        assert!(spec.validate().is_err());
    }}
}
#[test]
fn refinement_rejects_each_invalid_relative_tolerance() {
    let coarse=vec![Sample{time_s:0.,values:vec![0.]},Sample{time_s:1.,values:vec![1.]}];
    let fine=vec![coarse[0].clone(),Sample{time_s:0.5,values:vec![0.5]},coarse[1].clone()];
    for relative in [0.,-1.,f64::NAN,f64::INFINITY] {assert!(audit::compare_refinement(&coarse,&fine,0,1e-6,relative,"V").is_err());}
    assert!(audit::compare_refinement(&coarse,&fine,0,1e-6,1e-3,"V").is_ok());
}
#[test]
fn exponential_chain_rule_uses_nonunit_and_zero_inner_slopes() {
    for slope in [0.,-3.,0.25,2.] {for x in [-0.8,0.,0.7] {
        let expression=Expr::Exp(Box::new(Expr::Multiply(Box::new(Expr::Constant(slope)),Box::new(Expr::Input))));
        let (value,derivative)=expression.evaluate(x).unwrap();let reference=(slope*x).exp();
        assert!((value-reference).abs()<1e-14);assert!((derivative-slope*reference).abs()<1e-14);
    }}
}
#[test]
fn four_dimensional_grid_is_valid_five_is_not_even_with_one_value_each() {
    let dimensions=|n| (0..n).map(|i|ScalarSweep{element_id:format!("R{i}"),values:vec![1.]}).collect();
    assert_eq!(ScalarGrid{dimensions:dimensions(4)}.cardinality().unwrap(),1);
    assert!(ScalarGrid{dimensions:dimensions(5)}.validate().is_err());
}
#[test]
fn waveform_budgets_and_fm_deviation_limits_are_inclusive() {
    let pwl=|n|Waveform::Pwl{points:(0..n).map(|i|Point{time_s:i as f64,value:(i%2) as f64}).collect()};
    pwl(4096).validate().unwrap();assert!(pwl(4097).validate().is_err());
    let fm=|index|Waveform::FrequencyModulated{offset:0.,amplitude:1.,carrier_hz:10.,modulation_hz:2.,modulation_index:index,delay_s:0.,modulation_phase_rad:0.,carrier_phase_rad:0.};
    fm(5.).validate().unwrap();assert!(fm(5.5).validate().is_err());
}
#[test]
fn zero_fall_pulse_takes_low_value_at_the_right_hand_width_boundary() {
    let pulse=Waveform::Pulse{low:2.,high:7.,delay_s:0.,rise_s:0.,width_s:1.,fall_s:0.,period_s:3.};
    assert_eq!(pulse.at(0.999).unwrap(),7.);assert_eq!(pulse.at(1.).unwrap(),2.);assert_eq!(pulse.at(1.001).unwrap(),2.);
}
#[test]
fn single_point_parenthesized_pwl_is_a_valid_constant_source() {
    let request=import_ngspice_request("one point\nV1 a 0 pwl(0,2)\nR1 a 0 1k\n.tran 1u 2u\n.end","pwl-single").unwrap();
    let value=serde_json::to_value(request).unwrap();assert!(value.to_string().contains("pwl"));
    assert!(import_ngspice_request("bad\nV1 a 0 pwl(0,2,3)\nR1 a 0 1k\n.tran 1u 2u\n.end","pwl-invalid").is_err());
}
#[test]
fn trapezoidal_capacitor_audit_is_invariant_to_terminal_common_mode() {
    // C=2F, differential 1->3V in 1s: I=4A, delta energy=8J.
    // Independent sources provide -20J and +12J. No solver is used as oracle.
    for common in [0.,3.,-2.] {
        let c=parse(&format!("audit\nV1 a 0 {}\nVB b 0 {common}\nC1 a b 2\n.end",common+2.)).unwrap();
        let config=TransientConfig{stop_s:1.,max_step_s:1.,method:Method::Trapezoidal,sources:vec![SourceOverride{id:"V1".into(),waveform:Waveform::Pwl{points:vec![Point{time_s:0.,value:common+1.},Point{time_s:1.,value:common+3.}]}}],..Default::default()};
        let before=Sample{time_s:0.,values:vec![common+1.,common,-4.,4.]};let after=Sample{time_s:1.,values:vec![common+3.,common,-4.,4.]};
        let report=audit::configured_step(&c,&before,&after,&config,AuditOptions::default()).unwrap();assert!(!report.has_failures(),"{report:?}");
        let mut corrupt=after.clone();corrupt.values[2]=0.;assert!(audit::configured_step(&c,&before,&corrupt,&config,AuditOptions::default()).unwrap().has_failures());
    }
}
#[test]
fn newton_rejects_zero_negative_and_nonfinite_absolute_tolerances() {
    for absolute_tolerance in [0.,-1.,f64::NAN,f64::INFINITY] {assert!(NewtonOptions{absolute_tolerance,..Default::default()}.validate().is_err());}
}
#[test]
fn stationary_dynamic_state_needs_no_correction_and_full_step_is_not_damped() {
    let idle=compile(&parse("idle\nR1 a 0 1\n.end").unwrap()).unwrap();
    let (_,report)=newton::solve_backward_euler_step(&idle,&DenseLu,Default::default(),&[0.],0.1,idle.rhs()).unwrap();
    assert_eq!(report.iterations,0);assert_eq!(report.linear_solves,0);assert_eq!(report.update_inf,0.);
    let driven=compile(&parse("driven\nV1 a 0 1\nR1 a 0 1\n.end").unwrap()).unwrap();
    let (values,report)=newton::solve_backward_euler_step(&driven,&DenseLu,Default::default(),&[0.,0.],0.1,driven.rhs()).unwrap();
    assert!((values[0]-1.).abs()<1e-14);assert!((values[1]+1.).abs()<1e-14);
    assert_eq!(report.damped_steps,0);assert!(report.update_inf<=1.);
}



#[test]
fn mos_zero_drain_span_has_mode_independent_physical_slopes() {
    // At Vds=0, Id=gm=gmb=0 and gd=beta*max(overdrive,0).
    // Selecting the other identical source cannot change these values; signed
    // zero is not a distinct physical current in this API's SI contract.
    for p_channel in [false,true] {for gate in [0.2,1.4] {
        let sign=if p_channel {-1.}else{1.};
        let model=circuit_simulator::mos1::Mos1Model{p_channel,oxide_farads:0.,threshold_volts:sign*0.7,beta_amps_per_volt_squared:2e-4,body_effect_sqrt_volts:0.4,surface_potential_volts:0.6,channel_length_modulation_per_volt:0.03};
        let bias=[0.,sign*gate,0.,0.];let at=model.linearize(bias).unwrap();
        let gd=2e-4*(gate-0.7_f64).max(0.);
        assert_eq!(at.current_amps,0.);assert!((at.partials_siemens[0]-gd).abs()<1e-18);
        assert_eq!(at.partials_siemens[1],0.);assert!((at.partials_siemens[2]+gd).abs()<1e-18);assert_eq!(at.partials_siemens[3],0.);
        let mut plus=bias;let mut minus=bias;plus[0]=1e-7;minus[0]= -1e-7;
        let numerical=(model.linearize(plus).unwrap().current_amps-model.linearize(minus).unwrap().current_amps)/2e-7;
        assert!((numerical-gd).abs()<1e-10);
    }}
}

proptest::proptest! {
    #![proptest_config(proptest::test_runner::Config{cases:64,rng_seed:proptest::test_runner::RngSeed::Fixed(2026111802),failure_persistence:None,..Default::default()})]
    #[test]
    fn exponential_composition_matches_independent_finite_difference(slope in -3.0_f64..3.0, x in -1.0_f64..1.0) {
        let expression=Expr::Exp(Box::new(Expr::Multiply(Box::new(Expr::Constant(slope)),Box::new(Expr::Input))));
        let (_,derivative)=expression.evaluate(x).unwrap();
        let epsilon=1e-5;let finite_difference=((slope*(x+epsilon)).exp()-(slope*(x-epsilon)).exp())/(2.*epsilon);
        proptest::prop_assert!((derivative-finite_difference).abs()<1e-7*(1.+derivative.abs()));
    }
}
