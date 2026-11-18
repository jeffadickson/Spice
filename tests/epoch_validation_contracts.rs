//! Shared input-admission contracts exposed by the frozen 4,411-mutant epoch.
use circuit_simulator::{
    ac::ac_with_source_phasors, adaptive::AdaptiveOptions, api::AcSourcePhasor,
    backend::DenseLu, compile::compile, error::{ErrorCode, SimError, SimResult},
    linalg::{LinearSolver, Matrix}, netlist::parse, solver_contract::SparseComplexSystem,
};
use num_complex::Complex64;
use std::{io::Write, process::{Command, Stdio}, sync::atomic::{AtomicUsize, Ordering}};

#[test]
fn adaptive_relative_and_step_bounds_are_independently_enforced() {
    let good=AdaptiveOptions{relative:0.1,current_relative:None,voltage_absolute:1e-6,
        current_absolute:1e-9,min_step_s:1e-3,max_attempts:100};
    good.validate(1e-3).unwrap();
    for relative in [1.,2.] { assert!(AdaptiveOptions{relative,..good.clone()}.validate(1e-3).is_err()); }
    assert!(AdaptiveOptions{min_step_s:2e-3,..good}.validate(1e-3).is_err());
}

struct NoProviderWork(AtomicUsize);
impl LinearSolver for NoProviderWork {
    fn name(&self)->&'static str { "must-not-run" }
    fn solve(&self,_:&Matrix,_:&[f64])->SimResult<Vec<f64>> {
        self.0.fetch_add(1,Ordering::SeqCst);Err(SimError::invalid("provider invoked"))
    }
    fn solve_complex_csc(&self,_:&SparseComplexSystem,_:&[Complex64])->SimResult<Vec<Complex64>> {
        self.0.fetch_add(1,Ordering::SeqCst);Err(SimError::invalid("provider invoked"))
    }
    fn solve_complex_rhs(&self,_:&Matrix,_:&Matrix,_:&[Complex64])->SimResult<Vec<Complex64>> {
        self.0.fetch_add(1,Ordering::SeqCst);Err(SimError::invalid("provider invoked"))
    }
}

#[test]
fn bad_phasor_components_are_rejected_before_any_provider_work() {
    let plan=compile(&parse("AC\nV1 a 0 1\nR1 a 0 100\n.end").unwrap()).unwrap();
    for (real,imag) in [(f64::NAN,0.),(0.,f64::NAN),(f64::INFINITY,0.),
        (0.,f64::NEG_INFINITY),(f64::NAN,f64::NAN)] {
        let provider=NoProviderWork(AtomicUsize::new(0));
        let error=ac_with_source_phasors(&plan,&[10.],&[AcSourcePhasor{id:"V1".into(),real,imag}],&provider).unwrap_err();
        assert_eq!(error.code,ErrorCode::InvalidInput);
        assert_eq!(provider.0.load(Ordering::SeqCst),0);
    }
    // A valid control shows the fixture does not simply reject every request.
    let samples=ac_with_source_phasors(&plan,&[10.],&[AcSourcePhasor{id:"V1".into(),real:2.,imag:3.}],&DenseLu).unwrap();
    assert!((samples[0].values[0].real-2.).abs()<1e-12);assert!((samples[0].values[0].imag-3.).abs()<1e-12);
}

#[test]
fn cli_one_mib_boundary_is_inclusive_and_oversize_is_resource_exhausted() {
    let executable=env!("CARGO_BIN_EXE_circuit-simulator");
    for size in [1_048_575,1_048_576,1_048_577] {
        // JSON whitespace preserves the same valid divider request at every size.
        let mut bytes=include_bytes!("../examples/divider.json").to_vec();bytes.resize(size,b' ');
        let mut child=Command::new(executable).stdin(Stdio::piped()).stdout(Stdio::piped()).stderr(Stdio::piped()).spawn().unwrap();
        let mut input=child.stdin.take().unwrap();input.write_all(&bytes).unwrap();drop(input);
        let output=child.wait_with_output().unwrap();
        let result:serde_json::Value=serde_json::from_slice(&output.stdout).unwrap();
        if size<=1_048_576 {assert!(output.status.success());assert_eq!(result["complete"],true);}
        else {assert!(!output.status.success());assert_eq!(result["error"]["code"],"resource_exhausted");}
    }
}
