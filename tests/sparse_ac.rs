//! End-to-end direct sparse AC qualification with independent circuit equations.
use circuit_simulator::{
    ac::ac,
    backend_faer::FaerSparseLu,
    compile::compile,
    error::{ErrorCode, SimError, SimResult},
    linalg::{LinearSolver, Matrix},
    netlist::parse,
    solver_contract::SparseComplexSystem,
};
use num_complex::Complex64;
use std::sync::atomic::{AtomicUsize, Ordering};

struct DirectOnly(AtomicUsize);
impl LinearSolver for DirectOnly {
    fn name(&self) -> &'static str {
        "direct-complex-only"
    }
    fn solve(&self, _: &Matrix, _: &[f64]) -> SimResult<Vec<f64>> {
        panic!("linear AC used real dense solve")
    }
    fn solve_complex(&self, _: &Matrix, _: &Matrix, _: &[f64]) -> SimResult<Vec<Complex64>> {
        panic!("linear AC used dense complex solve")
    }
    fn solve_complex_csc(
        &self,
        system: &SparseComplexSystem,
        rhs: &[Complex64],
    ) -> SimResult<Vec<Complex64>> {
        self.0.fetch_add(1, Ordering::Relaxed);
        FaerSparseLu.solve_complex_csc(system, rhs)
    }
}

#[test]
fn linear_rc_ac_uses_direct_csc_and_matches_the_transfer_equation() {
    let plan =
        compile(&parse("title\nV1 in 0 1\nR1 in out 1k\nC1 out 0 1u\n.end").unwrap()).unwrap();
    let corner = 1. / (std::f64::consts::TAU * 1e-3);
    let frequencies = [corner / 10., corner, corner * 10.];
    let provider = DirectOnly(AtomicUsize::new(0));
    let samples = ac(&plan, &frequencies, &provider).unwrap();
    assert_eq!(provider.0.load(Ordering::Relaxed), 3);
    for (sample, frequency) in samples.iter().zip(frequencies) {
        let expected = Complex64::new(1., 0.) / Complex64::new(1., frequency / corner);
        let actual = Complex64::new(sample.values[1].real, sample.values[1].imag);
        assert!((actual - expected).norm() < 1e-12);
    }
}

struct TerminalSparse(ErrorCode);
impl LinearSolver for TerminalSparse {
    fn name(&self) -> &'static str {
        "terminal-sparse"
    }
    fn solve(&self, _: &Matrix, _: &[f64]) -> SimResult<Vec<f64>> {
        panic!("unexpected fallback")
    }
    fn solve_complex(&self, _: &Matrix, _: &Matrix, _: &[f64]) -> SimResult<Vec<Complex64>> {
        panic!("unexpected fallback")
    }
    fn solve_complex_csc(
        &self,
        _: &SparseComplexSystem,
        _: &[Complex64],
    ) -> SimResult<Vec<Complex64>> {
        Err(SimError::new(self.0.clone(), "original sparse failure"))
    }
}
#[test]
fn sparse_ac_failures_do_not_silently_change_the_numerical_method() {
    let plan = compile(&parse("title\nV1 out 0 1\nR1 out 0 1\n.end").unwrap()).unwrap();
    for code in [
        ErrorCode::NumericalFailure,
        ErrorCode::SingularMatrix,
        ErrorCode::Cancelled,
        ErrorCode::ResourceExhausted,
        ErrorCode::IoError,
    ] {
        let error = ac(&plan, &[1.], &TerminalSparse(code.clone())).unwrap_err();
        assert_eq!(error.code, code);
        assert_eq!(error.message, "original sparse failure");
    }
}
