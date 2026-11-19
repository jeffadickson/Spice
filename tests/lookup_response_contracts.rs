//! Challenge public admission/lookup boundaries without replacing numerical
//! answers with missing-data zeros. Scripted responses are deliberately invalid.
use circuit_simulator::{backend::DenseLu,error::{ErrorCode,SimError,SimResult},expression::Expr,
    linalg::{LinearSolver,Matrix},measurement::value_at,transient::Sample};
use num_complex::Complex64 as C;
use proptest::{prelude::*,test_runner::{Config,RngSeed,TestRunner}};
use std::sync::atomic::{AtomicUsize,Ordering};

#[test]
fn matrix_dimension_limits_and_malformed_shapes_are_independent() {
    for n in [1_usize,256] {Matrix{n,values:vec![0.;n*n]}.validate(&vec![0.;n]).unwrap();}
    for n in [0_usize,257] {
        assert_eq!(Matrix{n,values:vec![0.;n*n]}.validate(&vec![0.;n]).unwrap_err().code,ErrorCode::InvalidInput);
    }
    for matrix in [Matrix{n:2,values:vec![0.;3]},Matrix{n:2,values:vec![0.;5]}] {
        assert_eq!(matrix.validate(&[0.,0.]).unwrap_err().code,ErrorCode::InvalidInput);
    }
    let matrix=Matrix{n:1,values:vec![1.]};
    for rhs in [vec![],vec![1.,2.],vec![f64::NAN],vec![f64::INFINITY]] {
        assert_eq!(matrix.validate(&rhs).unwrap_err().code,ErrorCode::InvalidInput);
    }
}

struct ScriptedComplex {response:Vec<C>,calls:AtomicUsize}
impl LinearSolver for ScriptedComplex {
    fn name(&self)->&'static str {"invalid_complex_response_fixture"}
    fn solve(&self,_:&Matrix,_:&[f64])->SimResult<Vec<f64>> {
        Err(SimError::new(ErrorCode::Unsupported,"fixture has no real solve"))
    }
    fn solve_complex(&self,_:&Matrix,_:&Matrix,_:&[f64])->SimResult<Vec<C>> {
        self.calls.fetch_add(1,Ordering::Relaxed);Ok(self.response.clone())
    }
}

#[test]
fn complex_response_components_and_shape_are_validated_independently() {
    let real=Matrix{n:1,values:vec![1.]};let imag=Matrix{n:1,values:vec![0.]};
    for response in [vec![],vec![C::new(1.,0.);2],vec![C::new(f64::NAN,0.)],
        vec![C::new(0.,f64::NAN)],vec![C::new(f64::INFINITY,0.)],vec![C::new(0.,f64::INFINITY)]] {
        let fixture=ScriptedComplex{response,calls:AtomicUsize::new(0)};
        assert_eq!(fixture.solve_complex_rhs(&real,&imag,&[C::new(1.,0.)]).unwrap_err().code,ErrorCode::NumericalFailure);
        assert_eq!(fixture.calls.load(Ordering::Relaxed),1);
    }
    // An actual provider solves the scalar identity for the positive control.
    assert_eq!(DenseLu.solve_complex_rhs(&real,&imag,&[C::new(1.,0.)]).unwrap(),vec![C::new(1.,0.)]);
}

fn samples()->Vec<Sample> {(0..=2).map(|i|Sample{time_s:i as f64,values:vec![2.*i as f64+1.]}).collect()}

#[test]
fn measurement_empty_malformed_and_outside_queries_stay_typed_failures() {
    assert_eq!(value_at(&[],0,0.).unwrap_err().code,ErrorCode::InvalidInput);
    let valid=samples();
    for time in [f64::NAN,f64::INFINITY,-1.,3.] {
        let error=value_at(&valid,0,time).unwrap_err();
        assert_eq!(error.code,ErrorCode::InvalidInput);assert!(!error.message.is_empty());
    }
    for bad in [vec![Sample{time_s:0.,values:vec![]}],
        vec![Sample{time_s:0.,values:vec![f64::NAN]}],
        vec![Sample{time_s:0.,values:vec![1.]},Sample{time_s:0.,values:vec![2.]}]] {
        assert_eq!(value_at(&bad,0,0.).unwrap_err().code,ErrorCode::InvalidInput);
    }
}

#[test]
fn exact_measurement_knots_precede_strict_interior_interpolation() {
    let data=samples();
    for knot in 0..=2 {assert_eq!(value_at(&data,0,knot as f64).unwrap(),2.*knot as f64+1.);}
    for time in [0.25,1_f64.next_down(),1_f64.next_up(),1.75] {
        assert!((value_at(&data,0,time).unwrap()-(2.*time+1.)).abs()<2e-15);
    }
}

#[test]
fn inverse_trigonometric_slopes_match_circle_geometry() {
    let asin=Expr::Asin(Box::new(Expr::Input));let acos=Expr::Acos(Box::new(Expr::Input));
    // The3-4-5 triangle gives slope1/(4/5)=1.25 at x=3/5.
    assert!((asin.evaluate(0.6).unwrap().1-1.25).abs()<1e-13);
    assert!((acos.evaluate(0.6).unwrap().1+1.25).abs()<1e-13);
    let config=Config{cases:64,rng_seed:RngSeed::Fixed(2026111933),failure_persistence:None,..Default::default()};
    TestRunner::new(config).run(&(-14_i32..15),|tick| {
        let x=tick as f64/16.;let expected=1./(1.-x*x).sqrt();
        let (angle,slope)=asin.evaluate(x).unwrap();let (other,negative)=acos.evaluate(x).unwrap();
        prop_assert!((angle.sin()-x).abs()<1e-13);
        prop_assert!((other.cos()-x).abs()<1e-13);
        prop_assert!((slope-expected).abs()<1e-13);
        prop_assert!((negative+expected).abs()<1e-13);
        Ok(())
    }).unwrap();
    for x in [-1.,1.] {assert_eq!(asin.evaluate(x).unwrap_err().code,ErrorCode::InvalidInput);assert_eq!(acos.evaluate(x).unwrap_err().code,ErrorCode::InvalidInput);}
}

#[test]
fn inverse_and_log_recursion_guards_are_not_masked_by_domain_errors() {
    let mut inverse=Expr::Constant(0.);
    for _ in 0..64 {inverse=Expr::Asin(Box::new(inverse));}
    assert_eq!(inverse.evaluate(0.).unwrap(),(0.,0.));
    inverse=Expr::Asin(Box::new(inverse));
    assert_eq!(inverse.evaluate(0.).unwrap_err().code,ErrorCode::InvalidInput);
    let mut identity=Expr::Constant(0.);
    for _ in 0..32 {identity=Expr::Log(Box::new(Expr::Exp(Box::new(identity))));}
    assert_eq!(identity.evaluate(0.).unwrap(),(0.,0.));
    identity=Expr::Log(Box::new(Expr::Exp(Box::new(identity))));
    assert_eq!(identity.evaluate(0.).unwrap_err().code,ErrorCode::InvalidInput);
}

#[test]
fn absolute_value_preserves_the_sign_convention_and_recursion_bound() {
    let absolute=Expr::Abs(Box::new(Expr::Input));
    // Zero slope at the cusp is the explicit existing piecewise convention,
    // not a claim that the mathematical absolute value is differentiable there.
    for (input,value,slope) in [(-2.,2.,-1.),(0.,0.,0.),(2.,2.,1.)] {
        assert_eq!(absolute.evaluate(input).unwrap(),(value,slope));
    }
    let mut chain=Expr::Constant(0.);
    for _ in 0..64 {chain=Expr::Abs(Box::new(chain));}
    assert_eq!(chain.evaluate(0.).unwrap(),(0.,0.));
    chain=Expr::Abs(Box::new(chain));
    assert_eq!(chain.evaluate(0.).unwrap_err().code,ErrorCode::InvalidInput);
}
