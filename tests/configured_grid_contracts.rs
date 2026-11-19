//! Authored-grid admission is independent of successful trajectory retention.
//! A callback cancellation witnesses inclusive preflight without a huge output.
use circuit_simulator::{backend::DenseLu,circuit::{Circuit,Element},compile::compile,
    configured::{self,TransientConfig},error::{ErrorCode,SimError},ids::NodeId as N};
use proptest::{prelude::*,test_runner::{Config,RngSeed,TestRunner}};

fn options(times:Vec<f64>,step:f64,budget:usize)->TransientConfig {
    TransientConfig{stop_s:2.,max_step_s:step,max_steps:budget,accepted_times_s:Some(times),..Default::default()}
}

#[test]
fn two_endpoints_are_a_valid_single_interval_grid() {
    options(vec![0.,2.],2.,1).validate().unwrap();
    assert_eq!(options(vec![0.,1.,2.],2.,1).validate().unwrap_err().code,ErrorCode::InvalidInput);
}

#[test]
fn represented_step_roundoff_boundary_is_inclusive_and_scaled_by_stop() {
    // At stop=2 the published 32*epsilon*stop allowance is 64 ULPs of1.
    // Construct endpoints by bits, independently of the production expression.
    let edge=f64::from_bits(1_f64.to_bits()+64);
    options(vec![0.,edge,2.],1.,2).validate().unwrap();
    assert_eq!(options(vec![0.,edge.next_up(),2.],1.,2).validate().unwrap_err().code,ErrorCode::InvalidInput);
}

#[test]
fn seeded_dyadic_grids_use_the_exact_authored_interval_budget() {
    let config=Config{cases:64,rng_seed:RngSeed::Fixed(2026111921),failure_persistence:None,..Config::default()};
    TestRunner::new(config).run(&(0_u32..11),|power| {
        let steps=1_usize<<power;let step=2./steps as f64;
        let times=(0..=steps).map(|i|i as f64*step).collect();
        prop_assert!(options(times,step,steps).validate().is_ok(),"exact dyadic grid rejected");
        Ok(())
    }).unwrap();
}

#[test]
fn recording_preflight_admits_exact_limit_before_callback_cancellation() {
    // Twenty independent zero-excitation resistors have analytically zero
    // voltage. Use the actual LU provider, not a stubbed zero-solution solver.
    let circuit=Circuit{nodes:(0..=20).map(|i|i.to_string()).collect(),elements:(1..=20)
        .map(|i|Element::Resistor{id:format!("R{i}"),p:N(i),n:N(0),ohms:1.}).collect()};
    let plan=compile(&circuit).unwrap();
    assert_eq!(plan.unknown_count(),20);
    for points in [100_000_usize,100_001] {
        let grid=(0..points).map(|i|2.*i as f64/(points-1) as f64).collect();
        let config=options(grid,2.,points-1);
        let mut observed=0;
        let result=configured::run_observed(&plan,&DenseLu,&config,&false.into(),|sample| {
            observed+=1;
            assert_eq!(sample.time_s,0.);
            assert_eq!(sample.values,vec![0.;20]);
            Err(SimError::new(ErrorCode::Cancelled,"preflight witness cancelled"))
        });
        if points==100_000 {
            assert_eq!(observed,1);
            assert_eq!(result.err().expect("expected callback cancellation").code,ErrorCode::Cancelled);
        } else {
            assert_eq!(observed,0);
            assert_eq!(result.err().expect("expected recording preflight rejection").code,ErrorCode::ResourceExhausted);
        }
    }
}
