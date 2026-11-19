//! Independent admission and componentwise backward-error contracts.
//! These fixtures challenge a vendor success result without invoking a vendor.
use circuit_simulator::{dc,error::{ErrorCode,SimError,SimResult},
    linalg::{LinearSolver,Matrix},sparse::CscMatrix};
use proptest::{prelude::*,test_runner::{Config,RngSeed,TestRunner}};
use std::sync::atomic::{AtomicUsize,Ordering};

/// A tripwire only: no numerical result may be requested for invalid input.
#[derive(Default)]
struct AdmissionTripwire(AtomicUsize);
impl LinearSolver for AdmissionTripwire {
    fn name(&self)->&'static str {"admission_tripwire"}
    fn solve(&self,_:&Matrix,_:&[f64])->SimResult<Vec<f64>> {
        self.0.fetch_add(1,Ordering::Relaxed);
        Err(SimError::new(ErrorCode::NumericalFailure,"invalid input reached dense provider"))
    }
    fn solve_csc(&self,_:&CscMatrix,_:&[f64])->SimResult<Vec<f64>> {
        self.0.fetch_add(1,Ordering::Relaxed);
        Err(SimError::new(ErrorCode::NumericalFailure,"invalid input reached sparse provider"))
    }
}

#[test]
fn sparse_rhs_shape_and_finiteness_fail_independently_before_provider_calls() {
    let matrix=CscMatrix::new(2,vec![0,1,2],vec![0,1],vec![1.,1.]).unwrap();
    for rhs in [vec![1.],vec![1.,2.,3.],vec![f64::NAN,1.],vec![1.,f64::INFINITY],vec![f64::NEG_INFINITY,1.]] {
        let tripwire=AdmissionTripwire::default();
        assert_eq!(dc::solve_csc_verified(&matrix,&rhs,&tripwire).unwrap_err().code,ErrorCode::InvalidInput);
        assert_eq!(tripwire.0.load(Ordering::Relaxed),0);
    }
}

#[test]
fn dense_provider_response_requires_exact_shape_and_finite_components() {
    let matrix=Matrix{n:1,values:vec![1.]};
    for values in [vec![],vec![1.,2.],vec![f64::NAN],vec![f64::INFINITY],vec![f64::NEG_INFINITY]] {
        assert_eq!(dc::verify_solution(&matrix,&[1.],&values).unwrap_err().code,ErrorCode::NumericalFailure);
    }
    assert_eq!(dc::verify_solution(&matrix,&[1.],&[1.]).unwrap(),0.);
}

#[test]
fn cancellation_does_not_hide_overflow_of_the_absolute_residual_scale() {
    // A*x=(0,-1) is exactly representable: huge terms cancel in row0.
    // Its absolute scale2e308 is not representable. A zero reported error
    // would claim a numerical validation that could not actually be computed.
    let matrix=Matrix{n:2,values:vec![1e308,1e308,0.,1.]};
    assert_eq!(dc::verify_solution(&matrix,&[0.,-1.],&[1.,-1.]).unwrap_err().code,ErrorCode::NumericalFailure);
}

#[test]
fn row_scaling_preserves_the_independent_scalar_backward_error() {
    // A=2, x=3, b=6+delta: the exact componentwise ratio is delta/(12+delta).
    // Exact powers of two rescale row units without changing that ratio.
    let delta=2_f64.powi(-30);
    let expected=delta/(12.+delta);
    let config=Config{cases:64,rng_seed:RngSeed::Fixed(2026111925),failure_persistence:None,..Default::default()};
    TestRunner::new(config).run(&(-20_i32..21),|power| {
        let scale=2_f64.powi(power);
        let matrix=Matrix{n:1,values:vec![2.*scale]};
        let actual=dc::verify_solution(&matrix,&[(6.+delta)*scale],&[3.]).unwrap();
        prop_assert!((actual-expected).abs()<1e-24,"row-scaled error differs from scalar reference");
        Ok(())
    }).unwrap();
}

#[test]
fn exact_backward_error_limit_is_inclusive_but_the_next_solution_is_not() {
    // Half-integers below2^53 are exact. Difference=1, scale=10^10,
    // so the binary64 division rounds to the same value as literal1e-10.
    let matrix=Matrix{n:1,values:vec![1.]};
    let rhs=4_999_999_999.5;
    let solution=5_000_000_000.5_f64;
    assert_eq!(dc::verify_solution(&matrix,&[rhs],&[solution]).unwrap(),1e-10);
    assert_eq!(dc::verify_solution(&matrix,&[rhs],&[solution.next_up()]).unwrap_err().code,ErrorCode::NumericalFailure);
}
