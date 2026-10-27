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

#[test]
fn small_phasor_replaces_large_nominal_without_cancellation() {
    use circuit_simulator::{ac::ac_with_source_phasors, api::AcSourcePhasor, backend::DenseLu};
    for text in [
        "title\nV1 out 0 1e308\nR1 out 0 1k\n.end",
        "title\nI1 0 out 1e308\nR1 out 0 1k\n.end",
    ] {
        let plan = compile(&parse(text).unwrap()).unwrap();
        let voltage = text.contains("V1");
        let phasor = AcSourcePhasor {
            id: if voltage { "V1" } else { "I1" }.into(),
            real: 1.,
            imag: 2.,
        };
        for provider in [&FaerSparseLu as &dyn LinearSolver, &DenseLu] {
            let samples =
                ac_with_source_phasors(&plan, &[1.], std::slice::from_ref(&phasor), provider)
                    .unwrap();
            let expected = if voltage { 1. } else { 1000. };
            assert!((samples[0].values[0].real - expected).abs() < 1e-10);
            assert!((samples[0].values[0].imag - 2. * expected).abs() < 1e-10);
        }
    }
}

struct DirectOnly(AtomicUsize);

#[test]
fn complex_current_between_two_live_nodes_obeys_both_terminal_kcl_signs() {
    use circuit_simulator::{ac::ac_with_source_phasors, api::AcSourcePhasor, backend::DenseLu};
    let plan = compile(&parse("title\nRA a 0 2\nRB b 0 3\nI1 a b 0\n.end").unwrap()).unwrap();
    for provider in [&DenseLu as &dyn LinearSolver, &FaerSparseLu] {
        let source = AcSourcePhasor {
            id: "I1".into(),
            real: 2.,
            imag: -1.,
        };
        let samples = ac_with_source_phasors(&plan, &[1.], &[source], provider).unwrap();
        assert_eq!(samples[0].values[0].real, -4.);
        assert_eq!(samples[0].values[0].imag, 2.);
        assert_eq!(samples[0].values[1].real, 6.);
        assert_eq!(samples[0].values[1].imag, -3.);
    }
}

#[test]
fn phasor_validation_independently_rejects_each_bad_field_and_rhs_overflow() {
    use circuit_simulator::{ac::ac_with_source_phasors, api::AcSourcePhasor};
    let plan = compile(&parse("title\nR1 a 0 1\nI1 0 a 0\nI2 0 a 0\n.end").unwrap()).unwrap();
    let provider = DirectOnly(AtomicUsize::new(0));
    let source = |id: &str, real, imag| AcSourcePhasor {
        id: id.into(),
        real,
        imag,
    };
    for sources in [
        vec![source("", 1., 0.)],
        vec![source("missing", 1., 0.)],
        vec![source("R1", 1., 0.)],
        vec![source("I1", f64::NAN, 0.)],
        vec![source("I1", 0., f64::NAN)],
        vec![source("I1", f64::INFINITY, 0.)],
        vec![source("I1", 0., f64::NEG_INFINITY)],
        vec![source("I1", 0., 0.), source("I1", 1., 2.)],
        vec![source("I1", f64::MAX, 0.), source("I2", f64::MAX, 0.)],
        vec![source("I1", 0., f64::MAX), source("I2", 0., f64::MAX)],
    ] {
        assert!(ac_with_source_phasors(&plan, &[1.], &sources, &provider).is_err());
    }
    assert_eq!(provider.0.load(Ordering::Relaxed), 0);
}

// A budget probe for zero-excitation resistor networks. This is deliberately
// not numerical-provider qualification: exact zero satisfies every equation.
struct ZeroBudgetProbe(AtomicUsize);
impl LinearSolver for ZeroBudgetProbe {
    fn name(&self) -> &'static str {
        "zero-budget-probe"
    }
    fn solve(&self, _: &Matrix, _: &[f64]) -> SimResult<Vec<f64>> {
        panic!("unexpected real solve")
    }
    fn solve_complex_csc(
        &self,
        system: &SparseComplexSystem,
        rhs: &[Complex64],
    ) -> SimResult<Vec<Complex64>> {
        assert!(rhs.iter().all(|v| *v == Complex64::new(0., 0.)));
        self.0.fetch_add(1, Ordering::Relaxed);
        Ok(vec![Complex64::new(0., 0.); system.pattern().dimension()])
    }
}

#[test]
fn ac_grid_and_retained_scalar_limits_are_inclusive_and_checked_first() {
    let provider = ZeroBudgetProbe(AtomicUsize::new(0));
    let make_plan = |count| {
        let mut text = String::from("title\n");
        for index in 1..=count {
            text += &format!("R{index} n{index} 0 1\n");
        }
        compile(&parse(&(text + ".end")).unwrap()).unwrap()
    };
    let plan = make_plan(100);
    assert!(ac(&plan, &[], &provider).is_err());
    assert!(ac(&plan, &vec![1.; 10_001], &provider).is_err());
    assert!(ac(&plan, &vec![1.; 10_002], &provider).is_err());
    assert_eq!(
        ac(&make_plan(101), &vec![1.; 10_000], &provider)
            .unwrap_err()
            .code,
        ErrorCode::ResourceExhausted
    );
    assert_eq!(provider.0.load(Ordering::Relaxed), 0);
    assert_eq!(
        ac(&plan, &vec![1.; 10_000], &provider).unwrap().len(),
        10_000
    );
    assert_eq!(provider.0.load(Ordering::Relaxed), 10_000);
}

#[test]
fn ac_override_count_limit_accepts_128_and_rejects_larger_before_provider() {
    use circuit_simulator::{ac::ac_with_source_phasors, api::AcSourcePhasor};
    let mut text = String::from("title\nR1 out 0 1\n");
    let mut sources = Vec::new();
    for index in 1..=130 {
        text += &format!("I{index} out 0 0\n");
        sources.push(AcSourcePhasor {
            id: format!("I{index}"),
            real: 0.,
            imag: 0.,
        });
    }
    let plan = compile(&parse(&(text + ".end")).unwrap()).unwrap();
    let provider = ZeroBudgetProbe(AtomicUsize::new(0));
    for count in [129, 130] {
        assert!(ac_with_source_phasors(&plan, &[1.], &sources[..count], &provider).is_err());
    }
    assert_eq!(provider.0.load(Ordering::Relaxed), 0);
    assert!(ac_with_source_phasors(&plan, &[1.], &sources[..128], &provider).is_ok());
    assert_eq!(provider.0.load(Ordering::Relaxed), 1);
}

#[test]
fn independent_complex_residual_rejects_bad_provider_results() {
    use circuit_simulator::{ac::verify_complex_system, sparse::CscPattern};
    let system = SparseComplexSystem::new(
        CscPattern::new(1, vec![0, 1], vec![0]).unwrap(),
        vec![Complex64::new(2., 3.)],
    )
    .unwrap();
    let exact = [Complex64::new(1., -2.)];
    let rhs = [Complex64::new(8., -1.)];
    assert_eq!(verify_complex_system(&system, &rhs, &exact).unwrap(), 0.);
    for values in [
        vec![],
        vec![Complex64::new(0., 0.)],
        vec![Complex64::new(f64::NAN, 0.)],
        vec![Complex64::new(f64::MAX, f64::MAX)],
    ] {
        assert!(verify_complex_system(&system, &rhs, &values).is_err());
    }
    assert!(verify_complex_system(&system, &[], &exact).is_err());
    let zero =
        SparseComplexSystem::new(CscPattern::new(1, vec![0, 0], vec![]).unwrap(), vec![]).unwrap();
    assert_eq!(
        verify_complex_system(&zero, &[Complex64::new(0., 0.)], &exact).unwrap(),
        0.
    );
}

#[test]
fn complex_backward_error_is_invariant_under_equation_scaling() {
    use circuit_simulator::{ac::verify_complex_system, sparse::CscPattern};
    // A relative acceptance test must divide by the equation scale. Exact
    // solutions alone cannot distinguish an accidental multiplication here.
    for scale in [1e-120, 1e-9, 1e9, 1e120] {
        let system = SparseComplexSystem::new(
            CscPattern::new(1, vec![0, 1], vec![0]).unwrap(),
            vec![Complex64::new(scale, 0.)],
        )
        .unwrap();
        let rhs = [Complex64::new(scale, 0.)];
        let near = [Complex64::new(1. + 5e-11, 0.)];
        let inaccurate = [Complex64::new(1.01, 0.)];
        let error = verify_complex_system(&system, &rhs, &near).unwrap();
        assert!((error - 2.5e-11).abs() < 1e-16);
        assert!(verify_complex_system(&system, &rhs, &inaccurate).is_err());
    }
}

#[test]
fn sparse_incremental_diode_stamps_match_terminal_equations() {
    use circuit_simulator::newton::conductance_at_bias_csc;
    let plan = compile(
        &parse("title\nV1 in 0 1\nR1 in out 1k\nD1 out in 1p 25.85m\nD2 out 0 2p 30m\n.end")
            .unwrap(),
    )
    .unwrap();
    let bias = [0.2, 0.3, 0.];
    let sparse = conductance_at_bias_csc(&plan, &bias).unwrap();
    let mut actual = [[0.; 3]; 3];
    for ((row, column), value) in sparse.pattern().slots().zip(sparse.values()) {
        actual[row][column] = *value;
    }
    let g1 = 1e-12 / 0.02585 * (0.1_f64 / 0.02585).exp();
    let g2 = 2e-12 / 0.03 * (0.3_f64 / 0.03).exp();
    let expected = [
        [0.001 + g1, -0.001 - g1, 1.],
        [-0.001 - g1, 0.001 + g1 + g2, 0.],
        [1., 0., 0.],
    ];
    for (actual, expected) in actual.iter().flatten().zip(expected.iter().flatten()) {
        assert!((actual - expected).abs() < 1e-15);
    }
    assert!(conductance_at_bias_csc(&plan, &[]).is_err());
    assert!(conductance_at_bias_csc(&plan, &[f64::NAN, 0., 0.]).is_err());
    assert!(conductance_at_bias_csc(&plan, &[0., 1000., 0.]).is_err());
}

#[test]
fn invalid_grid_is_rejected_before_nonlinear_bias_work() {
    let plan = compile(&parse("title\nV1 in 0 1\nR1 in out 1k\nD1 out 0 1p 25.85m\n.end").unwrap())
        .unwrap();
    let provider = DirectOnly(AtomicUsize::new(0));
    for bad in [0., -1., f64::NAN, f64::INFINITY, f64::MAX] {
        assert!(ac(&plan, &[1., bad], &provider).is_err());
    }
    assert_eq!(provider.0.load(Ordering::Relaxed), 0);
}
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
