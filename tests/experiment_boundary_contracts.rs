//! Synthetic recorded-data fixtures qualify the measurement consumer, not a
//! circuit solution. Their manifest deliberately identifies no actual solver.
use circuit_simulator::{api::{Manifest,Signal},audit::AuditReport,assertion::Outcome,
    error::ErrorCode,experiment::{self,AssertionSpec,ExperimentSpec,MeasurementSpec,Operation},
    linalg::BackendCapabilities,transient::Sample};
use proptest::{prelude::*,test_runner::{Config,RngSeed,TestRunner}};

fn measurement(id:impl Into<String>,operation:Operation)->MeasurementSpec {
    MeasurementSpec{id:id.into(),signal:"v(test)".into(),operation}
}
fn assertion(id:impl Into<String>,low:f64,high:f64)->AssertionSpec {
    AssertionSpec{id:id.into(),measurement:"m0".into(),unit:"V".into(),low,high}
}
fn data(points:&[(f64,f64)])->Manifest {
    Manifest{consistency:AuditReport{checks:vec![]},diagnostics:vec![],schema_version:"1",
        case_id:"synthetic-measurement-fixture".into(),status:"synthetic_fixture",complete:false,
        backend:BackendCapabilities{id:"no_solver",placement:"fixture",max_unknowns:1,
            sparse:false,gpu:false,complex:false,prepared_real:false,provider_version:"none"},
        request_sha256:"not-a-simulation".into(),signals:vec![Signal{id:"v(test)".into(),quantity:"voltage",unit:"V"}],
        samples:points.iter().map(|&(time_s,value)|Sample{time_s,values:vec![value]}).collect(),
        ac_samples:vec![],integration:None,measurements:vec![],assertions:vec![],dc_power:None,newton:None}
}
fn rms(points:&[(f64,f64)])->f64 {
    let spec=ExperimentSpec{measurements:vec![measurement("m0",Operation::Rms{start_s:1.,stop_s:3.})],assertions:vec![]};
    let (values,_)=experiment::evaluate(&data(points),&spec).unwrap();
    assert_eq!(values[0].status,"valid");
    values[0].value.expect("finite analytic RMS must be available")
}

#[test]
fn experiment_capacities_are_inclusive_and_independently_bounded() {
    for count in [256_usize,257] {
        let spec=ExperimentSpec{measurements:(0..count).map(|i|measurement(format!("m{i}"),Operation::ValueAt{time_s:0.})).collect(),assertions:vec![]};
        if count==256 {spec.validate().unwrap();}else{assert_eq!(spec.validate().unwrap_err().code,ErrorCode::InvalidInput);}
    }
    for count in [512_usize,513] {
        let spec=ExperimentSpec{measurements:vec![measurement("m0",Operation::ValueAt{time_s:0.})],
            assertions:(0..count).map(|i|assertion(format!("a{i}"),0.,0.)).collect()};
        if count==512 {spec.validate().unwrap();}else{assert_eq!(spec.validate().unwrap_err().code,ErrorCode::InvalidInput);}
    }
}

#[test]
fn assertion_bounds_allow_equality_but_reject_inversion_and_nonfinite_values() {
    for (low,high,valid) in [(0.,0.,true),(1.,1.,true),(0.,1.,true),(2.,1.,false),
        (0.,f64::NAN,false),(0.,f64::INFINITY,false),(0.,f64::NEG_INFINITY,false),
        (f64::NAN,1.,false),(f64::INFINITY,1.,false),(f64::NEG_INFINITY,1.,false)] {
        let spec=ExperimentSpec{measurements:vec![measurement("m0",Operation::ValueAt{time_s:0.})],assertions:vec![assertion("a0",low,high)]};
        if valid {spec.validate().unwrap();}else{assert_eq!(spec.validate().unwrap_err().code,ErrorCode::InvalidInput);}
    }
}

#[test]
fn translated_window_operations_ignore_outside_samples_and_keep_units() {
    let config=Config{cases:64,rng_seed:RngSeed::Fixed(2026111929),failure_persistence:None,..Default::default()};
    TestRunner::new(config).run(&(1_u32..65,1_i32..9,any::<bool>()),|(tick,magnitude,negative)| {
        let start=tick as f64/4.;let stop=start+2.;let value=magnitude as f64*if negative {-1.}else{1.};
        let manifest=data(&[(0.,100.),(start,value),(start+1.,value),(stop,value),(stop+1.,-100.)]);
        let spec=ExperimentSpec{measurements:vec![measurement("min",Operation::Minimum{start_s:start,stop_s:stop}),
            measurement("max",Operation::Maximum{start_s:start,stop_s:stop}),
            measurement("integral",Operation::Integral{start_s:start,stop_s:stop}),
            measurement("rms",Operation::Rms{start_s:start,stop_s:stop})],assertions:vec![]};
        let (actual,_)=experiment::evaluate(&manifest,&spec).unwrap();
        for (i,expected) in [value,value,2.*value,value.abs()].into_iter().enumerate() {
            prop_assert_eq!(actual[i].status,"valid");
            prop_assert!((actual[i].value.unwrap()-expected).abs()<1e-12,"window measurement mismatch");
        }
        prop_assert_eq!(actual[2].unit.as_str(),"V*s");
        prop_assert_eq!(actual[3].unit.as_str(),"V");
        prop_assert_eq!(actual[3].value,Some(value.abs()));
        Ok(())
    }).unwrap();
}

#[test]
fn large_constant_rms_stays_finite_without_squaring_physical_amplitudes() {
    for amplitude in [1e154,f64::MAX] {
        let actual=rms(&[(1.,amplitude),(2.,amplitude),(3.,amplitude)]);
        assert!(actual.is_finite()&&actual<=amplitude);
        assert_eq!(actual,amplitude);
        assert!((actual/amplitude-1.).abs()<4e-15);
    }
}

#[test]
fn tiny_constant_rms_does_not_collapse_to_a_false_zero() {
    assert_eq!(rms(&[(1.,0.),(2.,0.),(3.,0.)]),0.);
    for amplitude in [1e-200,f64::MIN_POSITIVE,f64::from_bits(1)] {
        let actual=rms(&[(1.,amplitude),(2.,amplitude),(3.,amplitude)]);
        assert!(actual>0.,"positive constant RMS collapsed to zero");
        assert_eq!(actual,amplitude);
        assert!((actual/amplitude-1.).abs()<4e-15);
    }
}

#[test]
fn triangular_extreme_rms_matches_the_linear_interpolant() {
    let amplitude=1e154;
    let actual=rms(&[(1.,amplitude),(2.,-amplitude),(3.,amplitude)]);
    assert!((actual/amplitude-1./3_f64.sqrt()).abs()<4e-15);
}

#[test]
fn unusable_recordings_remain_unavailable_and_assertions_indeterminate() {
    for points in [vec![(1.,1.),(2.,f64::NAN),(3.,1.)],vec![(1.,1.),(1.,1.),(3.,1.)],vec![(1.,1.),(2.,1.)]] {
        let spec=ExperimentSpec{measurements:vec![measurement("m0",Operation::Rms{start_s:1.,stop_s:3.})],
            assertions:vec![assertion("a0",0.,2.)]};
        let (values,assertions)=experiment::evaluate(&data(&points),&spec).unwrap();
        assert_eq!(values[0].status,"unavailable");assert_eq!(values[0].value,None);
        assert!(values[0].reason.is_some());assert_eq!(assertions[0].outcome,Outcome::Indeterminate);
    }
}
