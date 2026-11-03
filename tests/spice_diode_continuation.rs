//! Qualification of the newly admitted charged junction through real recovery paths.
//! The oracle is inverse Shockley/KCL, independent of production model evaluation.
use circuit_simulator::{
    backend::{DenseLu, DenseQr}, backend_faer::FaerSparseLu,
    circuit::{Circuit, Element}, compile::compile, ids::NodeId,
    linalg::LinearSolver,
    newton::{solve, ContinuationOptions, GminContinuationOptions, NewtonOptions},
    spice_diode::SpiceDiodeModel,
};

fn circuit(amps: f64) -> Circuit {
    Circuit { nodes: vec!["0".into(), "out".into()], elements: vec![
        Element::Current { id:"I1".into(),p:NodeId(0),n:NodeId(1),amps },
        Element::SpiceDiode { id:"D1".into(),p:NodeId(1),n:NodeId(0),model:SpiceDiodeModel {
            saturation_amps:1e-12,thermal_volts:0.02585,junction_farads:2e-12,
            junction_volts:0.8,grading:0.5,forward_coefficient:0.5,transit_seconds:1e-9,
        }},
    ]}
}
fn policies() -> [NewtonOptions;2] {
    let base=NewtonOptions {max_iterations:8,..Default::default()};
    [NewtonOptions {continuation:Some(ContinuationOptions {
        initial_step:0.125,minimum_step:1e-6,max_stages:128,
    }),..base},NewtonOptions {gmin_continuation:Some(GminContinuationOptions {
        initial_siemens:1.0,minimum_siemens:1e-6,reduction_factor:0.5,max_stages:32,
    }),..base}]
}

#[test]
fn charged_profile_recovers_without_retaining_an_artificial_shunt() {
    let plan=compile(&circuit(1e-6)).unwrap();
    for provider in [&DenseLu as &dyn LinearSolver,&DenseQr,&FaerSparseLu] {
        // Establish a genuinely difficult start, rather than merely enabling a policy.
        assert!(solve(&plan,provider,NewtonOptions {max_iterations:8,..Default::default()}).is_err());
        for policy in policies() {
            let (v,report)=solve(&plan,provider,policy).unwrap();
            let expected=0.02585*(1.0_f64+1e-6/1e-12).ln();
            assert!((v[0]-expected).abs()<2e-7);
            assert!((1e-12*(v[0]/0.02585).exp_m1()-1e-6).abs()<2e-12);
            assert_eq!(report.gmin_siemens,None);
            if let Some(stages)=report.gmin_continuation {
                assert!(stages.final_unregularized);
                assert_eq!(stages.stages.last().unwrap().gmin_siemens,1e-6);
            } else {
                let stages=report.continuation.unwrap();
                assert!(stages.rejected_stages>0);
                assert_eq!(stages.stages.last().unwrap().source_scale,1.0);
            }
        }
    }
}

#[test]
fn charged_profile_cannot_turn_an_impossible_reverse_bias_into_success() {
    let plan=compile(&circuit(-1e-6)).unwrap();
    for provider in [&DenseLu as &dyn LinearSolver,&DenseQr,&FaerSparseLu] {
        for policy in policies() {assert!(solve(&plan,provider,policy).is_err());}
    }
}
