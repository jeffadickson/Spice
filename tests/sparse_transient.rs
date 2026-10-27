//! Direct real CSC contracts shared by DC and dynamic analysis drivers.
use circuit_simulator::{
    backend::DenseLu,
    backend_faer::FaerSparseLu,
    dc::{solve_csc_verified, verify_csc_solution},
    error::{ErrorCode, SimError, SimResult},
    linalg::{LinearSolver, Matrix},
    sparse::CscMatrix,
};

struct DirectReal;

#[test]
fn dynamic_sparse_steps_preserve_signed_flux_and_algebraic_rows() {
    use circuit_simulator::{
        compile::compile, configured::Method, linear_dynamic::assemble, netlist::parse,
    };
    let plan =
        compile(&parse("title\nV1 in 0 1\nR1 in out 2\nC1 out 0 3\nL1 out 0 4\n.end").unwrap())
            .unwrap();
    let previous = [1., 2., 3., 4.];
    // Node in, node out, source branch, inductor branch. Q_out,out=3,
    // Q_L,L=-4; the voltage constraint and input KCL are algebraic.
    for (method, alpha, expected) in [
        (Method::BackwardEuler, 2., [0., 12., 1., -32.]),
        (Method::Trapezoidal, 4., [0., 19.5, 1., -66.]),
    ] {
        let system = assemble(&plan, method, &previous, 0.5, plan.rhs(), plan.rhs()).unwrap();
        assert_eq!(system.rhs, expected);
        let mut observed = [[0.; 4]; 4];
        for (row, column, value) in system.matrix.entries() {
            observed[row][column] = value;
        }
        assert_eq!(
            observed,
            [
                [0.5, -0.5, 1., 0.],
                [-0.5, 0.5 + 3. * alpha, 0., 1.],
                [1., 0., 0., 0.],
                [0., 1., 0., -4. * alpha],
            ]
        );
    }
    assert_eq!(previous, [1., 2., 3., 4.]);
    for step in [0., -1., f64::NAN, f64::INFINITY, f64::from_bits(1)] {
        assert!(
            assemble(
                &plan,
                Method::BackwardEuler,
                &previous,
                step,
                plan.rhs(),
                plan.rhs()
            )
            .is_err()
        );
    }
    assert!(
        assemble(
            &plan,
            Method::BackwardEuler,
            &[],
            1.,
            plan.rhs(),
            plan.rhs()
        )
        .is_err()
    );
    assert!(assemble(&plan, Method::Trapezoidal, &previous, 1., &[], plan.rhs()).is_err());
}
impl LinearSolver for DirectReal {
    fn name(&self) -> &'static str {
        "direct-real-only"
    }
    fn solve(&self, _: &Matrix, _: &[f64]) -> SimResult<Vec<f64>> {
        panic!("unexpected dense solve")
    }
    fn solve_csc(&self, matrix: &CscMatrix, rhs: &[f64]) -> SimResult<Vec<f64>> {
        FaerSparseLu.solve_csc(matrix, rhs)
    }
}

#[test]
fn sparse_real_solve_and_residual_match_independent_equations() {
    let matrix = CscMatrix::new(2, vec![0, 2, 4], vec![0, 1, 0, 1], vec![2., 3., -1., 4.]).unwrap();
    for provider in [&DirectReal as &dyn LinearSolver, &DenseLu] {
        let (values, error) = solve_csc_verified(&matrix, &[0., 11.], provider).unwrap();
        assert!((values[0] - 1.).abs() < 1e-12);
        assert!((values[1] - 2.).abs() < 1e-12);
        assert!(error < 1e-15);
    }
    assert!(verify_csc_solution(&matrix, &[0., 11.], &[0., 0.]).is_err());
    assert!(verify_csc_solution(&matrix, &[0., 11.], &[]).is_err());
    assert!(verify_csc_solution(&matrix, &[0., 11.], &[f64::NAN, 0.]).is_err());
    assert!(solve_csc_verified(&matrix, &[], &DirectReal).is_err());
}

#[test]
fn sparse_real_error_is_scale_invariant_and_rejects_overflow() {
    for scale in [1e-120, 1e-9, 1e9, 1e120] {
        let matrix = CscMatrix::new(1, vec![0, 1], vec![0], vec![scale]).unwrap();
        assert!(
            (verify_csc_solution(&matrix, &[scale], &[1. + 5e-11]).unwrap() - 2.5e-11).abs()
                < 1e-16
        );
        assert!(verify_csc_solution(&matrix, &[scale], &[1.01]).is_err());
        assert!(verify_csc_solution(&matrix, &[f64::INFINITY], &[1.]).is_err());
    }
    let matrix = CscMatrix::new(1, vec![0, 1], vec![0], vec![f64::MAX]).unwrap();
    assert!(verify_csc_solution(&matrix, &[0.], &[2.]).is_err());
}

struct TerminalReal(ErrorCode);
impl LinearSolver for TerminalReal {
    fn name(&self) -> &'static str {
        "terminal-real"
    }
    fn solve(&self, _: &Matrix, _: &[f64]) -> SimResult<Vec<f64>> {
        panic!("unexpected fallback")
    }
    fn solve_csc(&self, _: &CscMatrix, _: &[f64]) -> SimResult<Vec<f64>> {
        Err(SimError::new(self.0.clone(), "original failure"))
    }
}
#[test]
fn terminal_real_errors_propagate_without_changing_providers() {
    let matrix = CscMatrix::new(1, vec![0, 1], vec![0], vec![1.]).unwrap();
    for code in [
        ErrorCode::SingularMatrix,
        ErrorCode::NumericalFailure,
        ErrorCode::ResourceExhausted,
        ErrorCode::Cancelled,
        ErrorCode::IoError,
    ] {
        let error = solve_csc_verified(&matrix, &[1.], &TerminalReal(code.clone())).unwrap_err();
        assert_eq!(error.code, code);
        assert_eq!(error.message, "original failure");
    }
}
