//! Public execution contracts use analytic resistive circuits and observable
//! provider calls, without asserting a particular Newton iteration count.
use circuit_simulator::{backend::DenseLu,circuit::{Circuit,Element},compile::compile,
    configured::{self,TransientConfig},expression::Expr,ids::NodeId as N,
    linalg::{LinearSolver,Matrix},error::SimResult};
use proptest::{prelude::*,test_runner::{Config,RngSeed,TestRunner}};
use std::sync::atomic::{AtomicUsize,Ordering};

fn options()->TransientConfig {
    TransientConfig{stop_s:1.,max_step_s:0.25,max_steps:16,..Default::default()}
}
fn passive()->Circuit {
    Circuit{nodes:["0","out"].map(str::to_owned).to_vec(),elements:vec![
        Element::Resistor{id:"R".into(),p:N(1),n:N(0),ohms:2.},
        Element::Current{id:"I".into(),p:N(0),n:N(1),amps:3.}]}
}

#[test]
fn fixed_dyadic_grid_advances_without_drift_or_missing_endpoints() {
    let plan=compile(&passive()).unwrap();
    let cfg=Config{cases:64,rng_seed:RngSeed::Fixed(2026111923),failure_persistence:None,..Default::default()};
    TestRunner::new(cfg).run(&(1_u32..7),|power| {
        let count=1_usize<<power;
        let config=TransientConfig{max_step_s:1./count as f64,max_steps:count,..options()};
        let result=configured::run(&plan,&DenseLu,&config,&false.into()).unwrap();
        prop_assert_eq!(result.samples.len(),count+1);
        prop_assert_eq!(result.report.accepted_steps,count);
        for (i,sample) in result.samples.iter().enumerate() {
            prop_assert_eq!(sample.time_s,i as f64/count as f64);
            prop_assert!((sample.values[0]-6.).abs()<1e-12);
        }
        Ok(())
    }).unwrap();
}

#[test]
fn explicit_grid_retains_a_distinct_penultimate_time_within_stop_roundoff() {
    let plan=compile(&passive()).unwrap();
    // Caller-owned distinct representable times may not be merged by the
    // fixed-grid endpoint snapping policy, even for a constant solution.
    let before=f64::from_bits(1_f64.to_bits()-2);
    let grid=vec![0.,0.5,before,1.];
    let config=TransientConfig{accepted_times_s:Some(grid.clone()),max_step_s:0.5,..options()};
    let result=configured::run(&plan,&DenseLu,&config,&false.into()).unwrap();
    assert_eq!(result.samples.iter().map(|s|s.time_s).collect::<Vec<_>>(),grid);
    assert_eq!(result.report.time_control,"accepted_time_grid");
}

/// Count real dense invocations while delegating every numerical answer to LU.
/// Leaving sparse preparation unsupported prevents hidden prepared-session work.
#[derive(Default)]
struct ObservedLu(AtomicUsize);
impl LinearSolver for ObservedLu {
    fn name(&self)->&'static str {"observed_lu"}
    fn solve(&self,matrix:&Matrix,rhs:&[f64])->SimResult<Vec<f64>> {
        self.0.fetch_add(1,Ordering::Relaxed);
        DenseLu.solve(matrix,rhs)
    }
}

#[test]
fn chained_behavioral_startup_obeys_polarity_domains_and_counts_seed_work() {
    // First source computes 3-1=2. The second takes log(2); a wrong startup
    // polarity is outside its real domain, rather than merely a worse seed.
    // Reverse the first source's terminals and its downstream control together.
    for reverse in [false,true] {
      // A common-mode translation preserves the2V differential drive. Include
      // translations where adding controls would instead seed zero/negative
      // voltage and violate the downstream logarithm's real domain.
      for (positive,negative) in [(3.,1.),(1.,-1.),(-1.,-3.)] {
        let (p,n,cp,cn)=if reverse {(N(0),N(1),N(0),N(1))}else{(N(1),N(0),N(1),N(0))};
        let circuit=Circuit{nodes:["0","first","second","cp","cn"].map(str::to_owned).to_vec(),elements:vec![
            Element::Voltage{id:"VP".into(),p:N(3),n:N(0),volts:positive},
            Element::Voltage{id:"VN".into(),p:N(4),n:N(0),volts:negative},
            // Reverse declaration order to require graph-based propagation.
            Element::BehavioralVoltage{id:"B2".into(),p:N(2),n:N(0),cp,cn,expression:Expr::Log(Box::new(Expr::Input))},
            Element::BehavioralVoltage{id:"B1".into(),p,n,cp:N(3),cn:N(4),expression:Expr::Input},
            Element::Resistor{id:"R1".into(),p:N(1),n:N(0),ohms:2.},
            Element::Resistor{id:"R2".into(),p:N(2),n:N(0),ohms:4.}]};
        let plan=compile(&circuit).unwrap();
        let solver=ObservedLu::default();
        let result=configured::run(&plan,&solver,&options(),&false.into()).unwrap();
        for sample in &result.samples {
            let expected=if reverse {-2.}else{2.};
            assert!((sample.values[0]-expected).abs()<1e-12);
            assert!((sample.values[1]-2_f64.ln()).abs()<1e-12);
        }
        assert_eq!(result.report.linear_solves,solver.0.load(Ordering::Relaxed));
        assert!(result.report.initial_newton.is_some());
      }
    }
}

#[test]
fn vccs_output_depends_on_differential_control_and_oriented_load_current() {
    // 0.5S*(3V-1V)=1A from output to ground. A 2ohm shunt therefore
    // requires -2V; flipping both output terminals reverses that voltage.
    for reverse in [false,true] {
        let (p,n)=if reverse {(N(0),N(1))}else{(N(1),N(0))};
        let circuit=Circuit{nodes:["0","out","cp","cn"].map(str::to_owned).to_vec(),elements:vec![
            Element::Voltage{id:"VP".into(),p:N(2),n:N(0),volts:3.},
            Element::Voltage{id:"VN".into(),p:N(3),n:N(0),volts:1.},
            Element::Vccs{id:"G".into(),p,n,cp:N(2),cn:N(3),siemens:0.5},
            Element::Resistor{id:"R".into(),p:N(1),n:N(0),ohms:2.}]};
        let plan=compile(&circuit).unwrap();
        let result=configured::run(&plan,&DenseLu,&options(),&false.into()).unwrap();
        for sample in &result.samples {assert!((sample.values[0]-if reverse {2.}else{-2.}).abs()<1e-12);}
    }
}

#[test]
fn nonlinear_trapezoidal_startup_restarts_once_then_obeys_endpoint_balance() {
    let circuit=Circuit{nodes:["0","out"].map(str::to_owned).to_vec(),elements:vec![
        Element::Current{id:"I".into(),p:N(0),n:N(1),amps:100e-6},
        Element::Resistor{id:"R".into(),p:N(1),n:N(0),ohms:1000.},
        Element::Capacitor{id:"C".into(),p:N(1),n:N(0),farads:1e-6},
        Element::Diode{id:"D".into(),p:N(1),n:N(0),saturation_amps:1e-6,thermal_volts:0.025}]};
    let plan=compile(&circuit).unwrap();
    let config=TransientConfig{stop_s:0.0003,max_step_s:0.0001,max_steps:8,
        method:configured::Method::Trapezoidal,initial_state:Some(vec![0.]),..Default::default()};
    let result=configured::run(&plan,&DenseLu,&config,&false.into()).unwrap();
    assert_eq!(result.report.backward_euler_restarts,vec![0.0001]);
    // Independent scalar constitutive equation: resistor plus Shockley diode
    // minus the injected100uA. Verify the actual integration balance in amps,
    // BE only at startup and endpoint-averaged conduction thereafter.
    let conduction=|v:f64|v/1000.+1e-6*(v/0.025).exp_m1()-100e-6;
    for (i,pair) in result.samples.windows(2).enumerate() {
        let (old,new)=(pair[0].values[0],pair[1].values[0]);
        let charge_rate=1e-6*(new-old)/(pair[1].time_s-pair[0].time_s);
        let balance=if i==0 {charge_rate+conduction(new)}
            else {2.*charge_rate+conduction(new)+conduction(old)};
        assert!(balance.abs()<1e-10,"interval{i} balance={balance}A");
    }
}
