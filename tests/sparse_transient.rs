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
fn sparse_adaptive_rejections_preserve_decay_and_only_observe_accepted_history() {
    use circuit_simulator::{
        adaptive::AdaptiveOptions,
        compile::compile,
        configured::{self, Method, TransientConfig},
        netlist::parse,
    };
    let plan = compile(&parse("title\nR1 out 0 2\nC1 out 0 3\n.end").unwrap()).unwrap();
    let provider = PreparedOnly(0.into());
    let options = TransientConfig {
        stop_s: 1.,
        max_step_s: 0.5,
        max_steps: 10000,
        method: Method::Trapezoidal,
        initial_state: Some(vec![1.]),
        adaptive: Some(AdaptiveOptions {
            relative: 1e-7,
            voltage_absolute: 1e-12,
            current_absolute: 1e-12,
            min_step_s: 1e-7,
            max_attempts: 10000,
        }),
        ..Default::default()
    };
    let mut observed = Vec::new();
    let result = configured::run_observed(&plan, &provider, &options, &false.into(), |sample| {
        observed.push(sample.time_s);
        Ok(())
    })
    .unwrap();
    assert!(result.report.rejected_steps > 0);
    assert!(result.report.max_accepted_error <= 1.);
    assert_eq!(
        observed,
        result.samples.iter().map(|s| s.time_s).collect::<Vec<_>>()
    );
    assert!(observed.windows(2).all(|pair| pair[1] > pair[0]));
    assert!((result.samples.last().unwrap().values[0] - (-1. / 6_f64).exp()).abs() < 1e-6);
    assert_eq!(
        provider.0.load(std::sync::atomic::Ordering::Relaxed),
        result.report.prepared_factors
    );
}

#[test]
fn changing_pwl_excitation_reuses_one_sparse_factor_without_reusing_old_rhs() {
    use circuit_simulator::{
        compile::compile,
        configured::{self, TransientConfig},
        netlist::parse,
        sources::SourceOverride,
        waveform::{Point, Waveform},
    };
    let plan = compile(&parse("title\nR1 out 0 2\nC1 out 0 3\nI1 0 out 0\n.end").unwrap()).unwrap();
    let options = TransientConfig {
        stop_s: 1.,
        max_step_s: 0.25,
        max_steps: 4,
        initial_state: Some(vec![0.]),
        sources: vec![SourceOverride {
            id: "I1".into(),
            waveform: Waveform::Pwl {
                points: vec![
                    Point {
                        time_s: 0.,
                        value: 0.,
                    },
                    Point {
                        time_s: 0.25,
                        value: 1.,
                    },
                    Point {
                        time_s: 0.5,
                        value: 1.,
                    },
                    Point {
                        time_s: 0.75,
                        value: -1.,
                    },
                    Point {
                        time_s: 1.,
                        value: 0.,
                    },
                ],
            },
        }],
        ..Default::default()
    };
    let provider = PreparedOnly(0.into());
    let result = configured::run(&plan, &provider, &options, &false.into()).unwrap();
    let mut expected = 0.;
    for (sample, current) in result.samples.iter().skip(1).zip([1., 1., -1., 0.]) {
        expected = (12. * expected + current) / 12.5;
        assert!((sample.values[0] - expected).abs() < 1e-12);
    }
    assert_eq!(result.report.prepared_factors, 1);
    assert_eq!(result.report.prepared_reuses, 3);
}

struct MisleadingPreparation(u8);
impl LinearSolver for MisleadingPreparation {
    fn name(&self) -> &'static str {
        "misleading-preparation"
    }
    fn solve(&self, _: &Matrix, _: &[f64]) -> SimResult<Vec<f64>> {
        panic!("terminal failure fell back")
    }
    fn prepare_csc(
        &self,
        matrix: &CscMatrix,
    ) -> SimResult<Option<circuit_simulator::linalg::PreparedSparseReal>> {
        use circuit_simulator::linalg::PreparedSparseReal;
        match self.0 {
            0 => Err(SimError::new(
                ErrorCode::IoError,
                "preparation transport failure",
            )),
            1 => Ok(Some(PreparedSparseReal::new(
                CscMatrix::new(1, vec![0, 1], vec![0], vec![99.]).unwrap(),
                |_| panic!("mismatched factor callback invoked"),
            ))),
            2 => Ok(Some(PreparedSparseReal::new(matrix.clone(), |_| {
                Ok(vec![0.])
            }))),
            3 => Ok(None),
            4 => Err(SimError::new(
                ErrorCode::Unsupported,
                "preparation unavailable",
            )),
            5 => Ok(Some(PreparedSparseReal::new(matrix.clone(), |_| {
                Err(SimError::new(ErrorCode::Cancelled, "factor cancelled"))
            }))),
            6 => Ok(Some(PreparedSparseReal::new(matrix.clone(), |_| {
                Ok(vec![])
            }))),
            _ => Ok(Some(PreparedSparseReal::new(matrix.clone(), |_| {
                Ok(vec![f64::NAN])
            }))),
        }
    }
    fn solve_csc(&self, _: &CscMatrix, _: &[f64]) -> SimResult<Vec<f64>> {
        Err(SimError::new(
            ErrorCode::ResourceExhausted,
            "direct provider budget",
        ))
    }
}

#[test]
fn configured_prepared_failures_never_publish_a_trial_or_hide_identity_errors() {
    use circuit_simulator::{
        compile::compile,
        configured::{self, TransientConfig},
        netlist::parse,
    };
    let plan = compile(&parse("title\nR1 out 0 2\nC1 out 0 3\n.end").unwrap()).unwrap();
    let options = TransientConfig {
        stop_s: 0.25,
        max_step_s: 0.25,
        max_steps: 1,
        initial_state: Some(vec![1.]),
        ..Default::default()
    };
    for (mode, expected) in [
        (0, ErrorCode::IoError),
        (1, ErrorCode::NumericalFailure),
        (2, ErrorCode::NumericalFailure),
        (3, ErrorCode::ResourceExhausted),
        (4, ErrorCode::ResourceExhausted),
        (5, ErrorCode::Cancelled),
        (6, ErrorCode::NumericalFailure),
        (7, ErrorCode::NumericalFailure),
    ] {
        let mut observed = Vec::new();
        let error = configured::run_observed(
            &plan,
            &MisleadingPreparation(mode),
            &options,
            &false.into(),
            |sample| {
                observed.push((sample.time_s, sample.values.clone()));
                Ok(())
            },
        )
        .err()
        .expect("bad preparation must fail");
        assert_eq!(error.code, expected);
        assert_eq!(observed, [(0., vec![1.])]);
        assert_eq!(options.initial_state.as_deref(), Some(&[1.][..]));
    }
}

struct PreparedOnly(std::sync::atomic::AtomicUsize);
impl LinearSolver for PreparedOnly {
    fn name(&self) -> &'static str {
        "prepared-csc-only"
    }
    fn solve(&self, _: &Matrix, _: &[f64]) -> SimResult<Vec<f64>> {
        panic!("dense solve")
    }
    fn prepare(&self, _: &Matrix) -> SimResult<Option<circuit_simulator::linalg::PreparedReal>> {
        panic!("dense preparation")
    }
    fn solve_csc(&self, _: &CscMatrix, _: &[f64]) -> SimResult<Vec<f64>> {
        panic!("uncached CSC solve")
    }
    fn prepare_csc(
        &self,
        matrix: &CscMatrix,
    ) -> SimResult<Option<circuit_simulator::linalg::PreparedSparseReal>> {
        self.0.fetch_add(1, std::sync::atomic::Ordering::Relaxed);
        FaerSparseLu.prepare_csc(matrix)
    }
}

#[test]
fn configured_sparse_factors_are_reused_for_be_and_trapezoidal_steps() {
    use circuit_simulator::{
        compile::compile,
        configured::{self, Method, TransientConfig},
        netlist::parse,
    };
    let plan = compile(&parse("title\nR1 out 0 2\nC1 out 0 3\n.end").unwrap()).unwrap();
    for (method, ratio) in [
        (Method::BackwardEuler, 24. / 25.),
        (Method::Trapezoidal, 47. / 49.),
    ] {
        let provider = PreparedOnly(0.into());
        let options = TransientConfig {
            stop_s: 1.,
            max_step_s: 0.25,
            max_steps: 4,
            method,
            initial_state: Some(vec![1.]),
            ..Default::default()
        };
        let result = configured::run(&plan, &provider, &options, &false.into()).unwrap();
        assert_eq!(result.report.prepared_factors, 1);
        assert_eq!(result.report.prepared_reuses, 3);
        assert_eq!(provider.0.load(std::sync::atomic::Ordering::Relaxed), 1);
        for (index, sample) in result.samples.iter().enumerate() {
            // Configured output includes its explicitly supplied initial sample.
            assert!((sample.values[0] - f64::powi(ratio, index as i32)).abs() < 1e-12);
        }
    }
}

#[test]
fn faer_prepares_canonical_numeric_factors_for_multiple_excitations() {
    let matrix = CscMatrix::new(2, vec![0, 2, 4], vec![0, 1, 0, 1], vec![2., 3., -1., 4.]).unwrap();
    let prepared = FaerSparseLu.prepare_csc(&matrix).unwrap().unwrap();
    assert_eq!(prepared.matrix().pattern(), matrix.pattern());
    assert_eq!(prepared.matrix().values(), matrix.values());
    for (rhs, expected) in [
        ([0., 11.], [1., 2.]),
        ([3., -1.], [1., -1.]),
        ([0., 0.], [0., 0.]),
    ] {
        let values = prepared.solve(&rhs).unwrap();
        for (value, expected) in values.iter().zip(expected) {
            assert!((value - expected).abs() < 1e-12);
        }
    }
    assert!(prepared.solve(&[1.]).is_err());
    let singular = CscMatrix::new(2, vec![0, 1, 1], vec![0], vec![1.]).unwrap();
    assert!(FaerSparseLu.prepare_csc(&singular).is_err());
}

#[test]
fn prepared_sparse_sessions_own_identity_and_check_every_callback_boundary() {
    use circuit_simulator::linalg::PreparedSparseReal;
    use std::{cell::Cell, rc::Rc};
    let calls = Rc::new(Cell::new(0));
    let observed = Rc::clone(&calls);
    let mut original = vec![2.];
    let matrix = CscMatrix::new(1, vec![0, 1], vec![0], original.clone()).unwrap();
    let prepared = PreparedSparseReal::new(matrix, move |rhs| {
        observed.set(observed.get() + 1);
        Ok(vec![rhs[0] / 2.])
    });
    original[0] = 99.;
    assert_eq!(prepared.matrix().values(), &[2.]);
    assert!(prepared.solve(&[]).is_err());
    assert!(prepared.solve(&[f64::NAN]).is_err());
    assert_eq!(calls.get(), 0);
    assert_eq!(prepared.solve(&[4.]).unwrap(), [2.]);
    assert_eq!(prepared.solve(&[-6.]).unwrap(), [-3.]);
    assert_eq!(calls.get(), 2);
    for bad in [vec![], vec![0.], vec![f64::INFINITY]] {
        let matrix = CscMatrix::new(1, vec![0, 1], vec![0], vec![2.]).unwrap();
        assert!(
            PreparedSparseReal::new(matrix, move |_| Ok(bad.clone()))
                .solve(&[4.])
                .is_err()
        );
    }
}

#[test]
fn legacy_be_uses_direct_sparse_solves_and_matches_discrete_rc_equation() {
    use circuit_simulator::{
        compile::compile,
        netlist::parse,
        transient::{TransientOptions, transient},
    };
    let plan =
        compile(&parse("title\nV1 in 0 1\nR1 in out 1k\nC1 out 0 1u\n.end").unwrap()).unwrap();
    let options = TransientOptions {
        stop_s: 0.001,
        step_s: 0.00025,
        max_steps: 4,
    };
    let samples = transient(&plan, &DirectReal, &options).unwrap();
    assert_eq!(samples.len(), 4);
    for (index, sample) in samples.iter().enumerate() {
        let expected = 1. - (1. / 1.25_f64).powi(index as i32 + 1);
        assert!((sample.values[1] - expected).abs() < 1e-12);
    }
    assert_eq!(
        transient(&plan, &TerminalReal(ErrorCode::Cancelled), &options)
            .unwrap_err()
            .code,
        ErrorCode::Cancelled
    );
}

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
