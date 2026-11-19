//! Componentwise complex boundaries, using exact small systems rather than a
//! production residual routine as the expected-answer oracle.
use circuit_simulator::{backend::DenseLu,error::ErrorCode,linalg::{LinearSolver,Matrix}};
use num_complex::Complex64 as C;
use proptest::{prelude::*,test_runner::{Config,RngSeed,TestRunner}};

#[test]
fn each_rhs_component_must_be_finite_independently() {
    let real=Matrix{n:1,values:vec![1.]};let imag=Matrix{n:1,values:vec![0.]};
    for bad in [f64::NAN,f64::INFINITY,f64::NEG_INFINITY] {
        for rhs in [C::new(bad,1.),C::new(1.,bad),C::new(bad,bad)] {
            let result=DenseLu.solve_complex_rhs(&real,&imag,&[rhs]);
            assert!(matches!(&result,Err(e) if e.code==ErrorCode::InvalidInput),"{rhs:?}: {result:?}");
        }
    }
    assert!(matches!(DenseLu.solve_complex_rhs(&real,&imag,&[]),Err(e) if e.code==ErrorCode::InvalidInput));
}

#[test]
fn overflow_in_either_solution_component_cannot_be_returned_as_success() {
    // A=.5, so finite b=(MAX,1) or (1,MAX) mathematically doubles to an
    // unrepresentable single component. No near-singular pivot is involved.
    let real=Matrix{n:1,values:vec![0.5]};let imag=Matrix{n:1,values:vec![0.]};
    for rhs in [C::new(f64::MAX,1.),C::new(1.,f64::MAX),C::new(-f64::MAX,1.),C::new(1.,-f64::MAX)] {
        let result=DenseLu.solve_complex_rhs(&real,&imag,&[rhs]);
        assert!(matches!(&result,Err(e) if e.code==ErrorCode::NumericalFailure),"{rhs:?}: {result:?}");
    }
}

#[test]
fn complex_known_solutions_preserve_both_components() {
    let real=Matrix{n:1,values:vec![0.5]};let imag=Matrix{n:1,values:vec![0.25]};
    let config=Config{cases:96,rng_seed:RngSeed::Fixed(2026111910),failure_persistence:None,..Config::default()};
    TestRunner::new(config).run(&(-16_i32..16,-16_i32..16),|(re,im)| {
        // Exact integer/quarter products form b=A*x independently. A=.5+.25j
        // is well-conditioned; this tests finite admission plus complex signs.
        let (re,im)=(re as f64,im as f64);
        let rhs=C::new(0.5*re-0.25*im,0.25*re+0.5*im);
        let x=DenseLu.solve_complex_rhs(&real,&imag,&[rhs]).unwrap();
        prop_assert_eq!(x.len(),1);
        prop_assert!((x[0].re-re).abs()<1e-13);
        prop_assert!((x[0].im-im).abs()<1e-13);
        Ok(())
    }).unwrap();
}
