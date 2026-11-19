//! Independent product-rule and terminal-coupling checks for forward Early charge.
use circuit_simulator::spice_diode::SpiceDiodeModel;
fn model() -> SpiceDiodeModel { SpiceDiodeModel { saturation_amps:1e-15,thermal_volts:0.02585,
    junction_farads:2e-12,junction_volts:0.75,grading:0.33,forward_coefficient:0.5,transit_seconds:2e-9 } }
#[test]
fn early_charge_has_both_exact_partials_and_preserves_depletion() {
    let m=model(); let be=0.62; let bc=-2.; let va=50.;
    let q=m.early_charge(be,bc,va).unwrap();
    let i=1e-15*((be/0.02585f64).exp()-1.);
    let dep=SpiceDiodeModel{transit_seconds:0.,..m}.charge(be).unwrap();
    assert!((q.charge_coulombs-(dep.charge_coulombs+2e-9*i*(1.-bc/va))).abs()<1e-24);
    assert!((q.control_farads+2e-9*i/va).abs()<1e-24);
    let h=1e-6;
    let db=(m.early_charge(be+h,bc,va).unwrap().charge_coulombs-m.early_charge(be-h,bc,va).unwrap().charge_coulombs)/(2.*h);
    let dc=(m.early_charge(be,bc+h,va).unwrap().charge_coulombs-m.early_charge(be,bc-h,va).unwrap().charge_coulombs)/(2.*h);
    assert!((db/q.capacitance_farads-1.).abs()<1e-8);
    assert!((dc/q.control_farads-1.).abs()<1e-7);
}
#[test]
fn reverse_bias_is_unmodulated_and_invalid_domains_fail() {
    let m=model();let q=m.early_charge(-0.2,-2.,50.).unwrap();
    assert_eq!(q.charge_coulombs,m.charge(-0.2).unwrap().charge_coulombs);
    assert_eq!(q.control_farads,0.);
    let zero=m.early_charge(0.,-2.,50.).unwrap();
    assert_eq!(zero.capacitance_farads,m.charge(0.).unwrap().capacitance_farads);
    assert_eq!(zero.control_farads,0.);
    for va in [0.,-1.,f64::NAN,f64::INFINITY]{assert!(m.early_charge(0.6,-2.,va).is_err());}
    assert!(m.early_charge(0.6,50.,50.).is_err());
}
fn request(analysis:serde_json::Value)->circuit_simulator::api::Request {
    serde_json::from_value(serde_json::json!({"schema_version":"1","case_id":"coupled-charge","backend":"lu",
        "circuit":{"nodes":["0","b","c"],"elements":[
        {"kind":"voltage","id":"VB","p":1,"n":0,"volts":0.62},
        {"kind":"voltage","id":"VC","p":2,"n":0,"volts":2.},
        {"kind":"junction_storage","id":"QBE","p":1,"n":0,"model":model(),"early_effect":{"cp":1,"cn":2,"volts":50.}}]},"analysis":analysis})).unwrap()
}
#[test]
fn control_only_ac_reaches_sparse_solver_and_reported_terminal_current() {
    for backend in ["lu","faer"] {
        let mut req=request(serde_json::json!({"kind":"ac","frequencies_hz":[1e6],"source_phasors":[{"id":"VB","real":0.,"imag":0.},{"id":"VC","real":1.,"imag":0.}]}));
        req.backend=backend.into();
        let r=circuit_simulator::api::run(&req).unwrap();
        let index=r.signals.iter().position(|s|s.id=="i(QBE)").unwrap();
        let q=&r.ac_samples[0].values[index];
        let expected=std::f64::consts::TAU*1e6*2e-9*1e-15*((0.62/0.02585f64).exp()-1.)/50.;
        assert!(q.real.abs()<1e-18);assert!((q.imag/expected-1.).abs()<1e-12,"{} vs {} ({})",q.imag,expected,backend);
        // VB's branch supplies exactly the opposite displacement; VC is an ideal control.
        assert!((r.ac_samples[0].values[2].imag+q.imag).abs()<1e-18);
        assert!(r.ac_samples[0].values[3].imag.abs()<1e-18);
    }
}
#[test]
fn quadrature_control_current_obeys_jomega_charge_and_terminal_kcl() {
    // With the base AC-grounded, dQ/dVc = TF*Is*expm1(Vbe/Vt)/VA.
    // Thus Icpl = j*omega*k*Vc: an imaginary drive produces a negative real
    // current. The earlier real-only drive cannot detect that sign/phase path.
    let k = 2e-9 * 1e-15 * (0.62 / 0.02585_f64).exp_m1() / 50.;
    for backend in ["lu", "faer"] {
        for (re, im) in [(0., 1.), (0.7, -0.3), (-0.2, 0.8)] {
            let mut req = request(serde_json::json!({"kind":"ac",
                "frequencies_hz":[1e3, 1e6], "source_phasors":[
                {"id":"VB","real":0.,"imag":0.},
                {"id":"VC","real":re,"imag":im}]}));
            req.backend = backend.into();
            let result = circuit_simulator::api::run(&req).unwrap();
            let at = |name: &str| result.signals.iter().position(|s| s.id == name).unwrap();
            let iq = at("i(QBE)");
            let ib = at("i(VB)");
            let ic = at("i(VC)");
            for sample in &result.ac_samples {
                let wk = std::f64::consts::TAU * sample.frequency_hz * k;
                let current = &sample.values[iq];
                let tolerance = 1e-12 * wk;
                assert!((current.real + wk * im).abs() < tolerance, "real {backend}");
                assert!((current.imag - wk * re).abs() < tolerance, "imag {backend}");
                // The base source supplies the storage current. The ideal
                // voltage control draws no current at either complex phase.
                assert!((sample.values[ib].real + current.real).abs() < tolerance);
                assert!((sample.values[ib].imag + current.imag).abs() < tolerance);
                assert!(sample.values[ic].real.abs() < tolerance);
                assert!(sample.values[ic].imag.abs() < tolerance);
            }
        }
    }
}
#[test]
fn be_residual_cross_jacobian_and_history_are_consistent_without_state_mutation() {
    let req=request(serde_json::json!({"kind":"operating_point"}));
    let plan=circuit_simulator::compile::compile(&req.circuit).unwrap();
    let old=vec![0.62,2.,0.,0.]; let mut trial=old.clone();trial[1]=2.1;
    let before=old.clone(); let h=1e-10;
    let rhs=vec![0.,0.,0.62,2.1];
    let s=circuit_simulator::newton::linearize_backward_euler(&plan,&trial,&old,h,&rhs).unwrap();
    let expected=2e-9*1e-15*((0.62/0.02585f64).exp()-1.)*(0.1/50.)/h;
    assert!((s.residual[0]/expected-1.).abs()<1e-10);
    let eps=1e-5; let mut plus=trial.clone();plus[1]+=eps;let mut minus=trial.clone();minus[1]-=eps;
    let a=circuit_simulator::newton::linearize_backward_euler(&plan,&plus,&old,h,&rhs).unwrap();
    let b=circuit_simulator::newton::linearize_backward_euler(&plan,&minus,&old,h,&rhs).unwrap();
    let fd=(a.residual[0]-b.residual[0])/(2.*eps);
    assert!((fd/s.jacobian.values[1]-1.).abs()<1e-7);
    assert_eq!(old,before);
    let again=circuit_simulator::newton::linearize_backward_euler(&plan,&trial,&old,h,&rhs).unwrap();
    assert_eq!(again.residual,s.residual);
}
#[test]
fn invalid_wire_control_nodes_are_rejected_before_compilation() {
    let mut req=request(serde_json::json!({"kind":"operating_point"}));
    if let circuit_simulator::circuit::Element::JunctionStorage{early_effect:Some(c),..}=&mut req.circuit.elements[2]{c.cp=circuit_simulator::ids::NodeId(99);}
    assert!(circuit_simulator::api::run(&req).is_err());
}
#[test]
fn trap_and_be_share_coupled_charge_and_failed_trials_preserve_committed_state() {
    use circuit_simulator::{backend::DenseLu,backend_faer::FaerSparseLu,linalg::LinearSolver,newton};
    let req=request(serde_json::json!({"kind":"operating_point"}));
    let plan=circuit_simulator::compile::compile(&req.circuit).unwrap();
    for solver in [&DenseLu as &dyn LinearSolver,&FaerSparseLu] {
        let old=vec![0.62,2.,0.,0.];let saved=old.clone();let old_rhs=vec![0.,0.,0.62,2.];
        let rhs=vec![0.,0.,0.62,2.1];let h=1e-10;
        let delta=2e-9*1e-15*((0.62/0.02585f64).exp()-1.)*0.1/50.;
        let (be,_)=newton::solve_backward_euler_step(&plan,solver,Default::default(),&old,h,&rhs).unwrap();
        let (trap,_)=newton::solve_trapezoidal_step(&plan,solver,Default::default(),&old,h,&rhs,&old_rhs).unwrap();
        assert!((be[2]+delta/h).abs()<1e-12);
        assert!((trap[2]+2.*delta/h).abs()<1e-12);
        assert_eq!(old,saved);
        let bad=vec![0.,0.,0.62,-100.];
        assert!(newton::solve_backward_euler_step(&plan,solver,Default::default(),&old,h,&bad).is_err());
        assert_eq!(old,saved);
        let (again,_)=newton::solve_backward_euler_step(&plan,solver,Default::default(),&old,h,&rhs).unwrap();
        assert_eq!(be,again);
    }
}
#[test]
fn configured_control_ramp_checks_independent_kcl_and_midpoint_probe_history() {
    for (method,adaptive) in [("backward_euler",false),("trapezoidal",false),("backward_euler",true),("trapezoidal",true)] {
        let mut options=serde_json::json!({"stop_s":1e-9,"max_step_s":1e-10,"max_steps":1000,"method":method,
            "sources":[{"id":"VC","waveform":{"kind":"pwl","points":[{"time_s":0.,"value":2.},{"time_s":1e-9,"value":2.5}]}}]});
        if adaptive { options["adaptive"]=serde_json::json!({"relative":1e-3,"voltage_absolute":1e-6,"current_absolute":1e-10,"min_step_s":1e-13,"max_attempts":2000}); }
        let req=request(serde_json::json!({"kind":"transient_configured","options":options}));
        let result=circuit_simulator::api::run(&req).unwrap();
        let qi=result.signals.iter().position(|s|s.id=="i(QBE)").unwrap();
        for sample in &result.samples {
            assert!((sample.values[qi]+sample.values[2]).abs()<1e-10,"{} {}",method,adaptive);
        }
        assert!(!result.consistency.has_failures());
        // Challenge the independent interval audit with a corrupted accepted source current.
        let plan=circuit_simulator::compile::compile(&req.circuit).unwrap();
        let circuit_simulator::api::Analysis::TransientConfigured{options}=req.analysis else{unreachable!()};
        let run=circuit_simulator::configured::run(&plan,&circuit_simulator::backend::DenseLu,&options,&false.into()).unwrap();
        let mut corrupt=run.samples[1].clone();corrupt.values[2]+=1e-5;
        let audit=circuit_simulator::audit::configured_step(&req.circuit,&run.samples[0],&corrupt,&options,Default::default()).unwrap();
        // Adaptive midpoint-dependent independent audits are explicitly unavailable.
        if !adaptive { assert!(audit.has_failures()); }
    }
}
#[test]
fn generated_bjt_ac_with_grounded_zero_phasor_supply_passes_both_providers() {
    let original: circuit_simulator::api::Request=serde_json::from_str(include_str!("../examples/bjt-early-charge-ac.json")).unwrap();
    for (backend,supply) in [("lu",3.),("faer",3.),("faer",12.)] {
        let mut request=original.clone();request.backend=backend.into();
        if let circuit_simulator::circuit::Element::Voltage{volts,..}=&mut request.circuit.elements[1]{*volts=supply;}
        let response=circuit_simulator::api::run(&request);
        assert!(response.is_ok(),"{} {:?}",backend,response.err());
    }
}
proptest::proptest! {
    #![proptest_config(proptest::test_runner::Config{cases:128,rng_seed:proptest::test_runner::RngSeed::Fixed(2026111307),..Default::default()})]
    #[test]
    fn independent_product_rule_holds(be in 0.05f64..0.7,bc in -10f64..0.0,va in 10f64..100.) {
        let m=model();let q=m.early_charge(be,bc,va).unwrap();let h=1e-5;
        let a=m.early_charge(be,bc+h,va).unwrap().charge_coulombs;
        let b=m.early_charge(be,bc-h,va).unwrap().charge_coulombs;
        proptest::prop_assert!(((a-b)/(2.*h)-q.control_farads).abs()<1e-20);
    }
}
