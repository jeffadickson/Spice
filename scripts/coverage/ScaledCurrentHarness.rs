//! Targeted LLVM harness compiling actual production law/error modules. This is
//! not whole-library or provider coverage; integration tests remain separate.
#[path="../../src/error.rs"] mod error;
#[path="../../src/voltage_scaled_cccs.rs"] mod law;
#[test]
fn legacy_zero_offset_and_error_contracts() {
    assert_eq!(law::linearize(2.,0.25,-0.01,2.).unwrap().current_gain,3.);
    let product=law::linearize_with_offset(2.,0.25,0.,-0.01,2.).unwrap();
    assert!((product.current_amps+0.01).abs()<1e-16);
    for voltage in [1e-200,1e-20,-1e-20,1e20] {
        let c=law::linearize_with_offset(1.,1.,0.,1.,voltage).unwrap();
        assert_ne!(c.current_amps,0.);assert_eq!(c.current_amps,voltage);
    }
    for args in [(f64::NAN,1.,1.,1.,1.),(1.,f64::NAN,1.,1.,1.),(1.,1.,f64::NAN,1.,1.),(1.,1.,1.,f64::INFINITY,1.),(1.,1.,1.,1.,f64::NAN),(f64::MAX,2.,0.,2.,2.)] {
        assert!(law::linearize_with_offset(args.0,args.1,args.2,args.3,args.4).is_err());
    }
}
proptest::proptest! {
    #![proptest_config(proptest::test_runner::Config {cases:128,rng_seed:proptest::test_runner::RngSeed::Fixed(2026111513),..Default::default()})]
    #[test]
    fn derivatives_match_independent_expanded_law(gain in -10f64..10.,k in -2f64..2.,offset in -2f64..2.,i in -0.1f64..0.1,v in -10f64..10.) {
        let c=law::linearize_with_offset(gain,k,offset,i,v).unwrap();
        let independent=|i:f64,v:f64|gain*offset*i+gain*k*i*v;
        let di=(independent(i+1e-6,v)-independent(i-1e-6,v))/2e-6;
        let dv=(independent(i,v+1e-5)-independent(i,v-1e-5))/2e-5;
        proptest::prop_assert!((c.current_gain-di).abs()<1e-8);
        proptest::prop_assert!((c.control_siemens-dv).abs()<1e-9);
    }
}
