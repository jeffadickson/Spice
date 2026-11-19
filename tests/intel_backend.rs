#![cfg(feature = "intel-opencl")]
use circuit_simulator::{linalg::Matrix, registry::BackendRegistry};
use num_complex::Complex64;
use proptest::prelude::*;

// The native context is process-scoped. Shut down once after the entire
// contract, including panic unwinding, matching the one-shot CLI lifetime.
struct Session;
impl Drop for Session { fn drop(&mut self) { spice_intel_host::shutdown().unwrap(); } }

#[test]
fn actual_intel_circuit_contract() {
    let _session=Session;
    let solver=BackendRegistry::reference().get("intel").unwrap();
    assert!(solver.capabilities().gpu);
    assert_eq!(solver.capabilities().placement,"gpu");
    assert_eq!(solver.name(),"intel_opencl_viennacl_gmres_experimental");
    assert_eq!(solver.capabilities().id,solver.name());
    // One complex unknown embeds into TWO real unknowns. Existing two-node
    // fixtures alone cannot distinguish 2*n from 2+n in the embedding size.
    let scalar=Matrix{n:1,values:vec![0.5]};
    let quadrature=Matrix{n:1,values:vec![0.25]};
    let value=solver.solve_complex_rhs(&scalar,&quadrature,&[Complex64::new(1.75,-1.)]).unwrap();
    assert_eq!(value.len(),1);
    assert!((value[0]-Complex64::new(2.,-3.)).norm()<1e-10);
    // Each malformed dimension/component must be rejected independently,
    // before the host is asked to solve an incompatible embedded matrix.
    let two=Matrix{n:2,values:vec![1.,0.,0.,1.]};
    for (imag,rhs) in [
        (&two,vec![Complex64::new(1.,0.)]),
        (&quadrature,vec![]),
        (&quadrature,vec![Complex64::new(f64::NAN,1.)]),
        (&quadrature,vec![Complex64::new(1.,f64::NAN)]),
        (&quadrature,vec![Complex64::new(f64::INFINITY,1.)]),
        (&quadrature,vec![Complex64::new(1.,f64::NEG_INFINITY)]),
    ] {
        let result=solver.solve_complex_rhs(&scalar,imag,&rhs);
        assert!(matches!(&result,Err(e) if e.code==circuit_simulator::error::ErrorCode::InvalidInput),"{rhs:?}: {result:?}");
    }
    assert!((solver.solve(&Matrix{n:1,values:vec![0.001]},&[0.001]).unwrap()[0]-1.).abs()<1e-10);
    // A source constraint has a zero diagonal; SPD-only or unpivoted LU
    // cannot substitute for a general real/complex circuit solve.
    let a=Matrix{n:2,values:vec![0.001,1.,1.,0.]};
    let x=solver.solve(&a,&[0.,5.]).unwrap();
    assert!((x[0]-5.).abs()<1e-10 && (x[1]+0.005).abs()<1e-12);
    let zero=solver.solve(&a,&[0.005,0.]).unwrap();
    assert_eq!(zero[0],0.);
    assert!((zero[1]-0.005).abs()<1e-12);
    let imag=Matrix{n:2,values:vec![0.01,0.,0.,0.]};
    let x=solver.solve_complex_rhs(&a,&imag,&[Complex64::new(0.,0.),Complex64::new(5.,2.)]).unwrap();
    assert!((x[0]-Complex64::new(5.,2.)).norm()<1e-10);
    assert!((x[1]+Complex64::new(0.001,0.01)*x[0]).norm()<1e-10);
    let real_drive=solver.solve_complex(&a,&imag,&[0.,5.]).unwrap();
    assert_eq!(real_drive[0].im,0.);
    assert!((real_drive[0].re-5.).abs()<1e-10);
    assert!(solver.solve(&Matrix{n:2,values:vec![1.]},&[1.,2.]).is_err());
    assert!(solver.solve(&Matrix{n:1,values:vec![f64::NAN]},&[1.]).is_err());
    assert!(solver.solve(&Matrix{n:2,values:vec![1.,1.,1.,1.]},&[1.,2.]).is_err());
    // Exercise compilation, assembly, physical audits and result descriptors.
    let mut request:circuit_simulator::api::Request=serde_json::from_str(include_str!("../examples/divider.json")).unwrap();
    request.backend="lu".into();
    let cpu=serde_json::to_value(circuit_simulator::api::run(&request).unwrap()).unwrap();
    request.backend="intel".into();
    let gpu=serde_json::to_value(circuit_simulator::api::run(&request).unwrap()).unwrap();
    assert_eq!(gpu["backend"]["gpu"],true);
    assert_eq!(gpu["backend"]["placement"],"gpu");
    let expected=cpu["samples"][0]["values"].as_array().unwrap();
    let got=gpu["samples"][0]["values"].as_array().unwrap();
    assert_eq!(expected.len(),got.len());
    for(a,b)in expected.iter().zip(got){assert!((a.as_f64().unwrap()-b.as_f64().unwrap()).abs()<1e-10);}
    // Exercise the new time-control option on an actual RC circuit. This
    // deliberately coarse grid checks provider parity, not global accuracy.
    let mut grid:circuit_simulator::api::Request=serde_json::from_str(include_str!("../examples/rc-accepted-grid.json")).unwrap();
    let reference=circuit_simulator::api::run(&grid).unwrap();
    grid.backend="intel".into();let actual=circuit_simulator::api::run(&grid).unwrap();
    assert_eq!(actual.integration.as_ref().unwrap().time_control,"accepted_time_grid");
    assert_eq!(actual.samples.len(),reference.samples.len());
    for(a,b) in actual.samples.iter().zip(&reference.samples){
        assert_eq!(a.time_s.to_bits(),b.time_s.to_bits());
        for(x,y) in a.values.iter().zip(&b.values){assert!((x-y).abs()<1e-10);}
    }
    let config=ProptestConfig{cases:16,rng_seed:proptest::test_runner::RngSeed::Fixed(2026111404),failure_persistence:None,..ProptestConfig::default()};
    let mut runner=proptest::test_runner::TestRunner::new(config);
    runner.run(&(2usize..9,prop::collection::vec(-100i32..100,64),prop::collection::vec(-20i32..20,8)),|(n,coefficients,known)|{
        let mut a=Matrix{n,values:vec![0.;n*n]};
        let x:Vec<_>=known[..n].iter().map(|v|f64::from(*v)/8.).collect();
        for i in 0..n{let mut sum=1.;for j in 0..n{if i!=j{let v=f64::from(coefficients[i*n+j])/128.;a.values[i*n+j]=v;sum+=v.abs();}}a.values[i*n+i]=sum;}
        let b:Vec<_>=(0..n).map(|i|(0..n).map(|j|a.values[i*n+j]*x[j]).sum()).collect();
        let got=solver.solve(&a,&b).unwrap();
        for i in 0..n{prop_assert!((got[i]-x[i]).abs()<1e-10);}
        Ok(())
    }).unwrap();
}
