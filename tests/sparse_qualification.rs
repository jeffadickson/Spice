//! Physical circuit oracles and adversarial providers, rather than vendor parity alone.
use circuit_simulator::{
    ac::ac_with_source_phasors,
    api::AcSourcePhasor,
    backend::DenseLu,
    backend_faer::FaerSparseLu,
    compile::compile,
    configured::{Method, TransientConfig, run},
    error::{ErrorCode, SimError, SimResult},
    linalg::{LinearSolver, Matrix, PreparedSparseReal},
    netlist::parse,
    solver_contract::SparseComplexSystem,
    sources::SourceOverride,
    sparse::CscMatrix,
    waveform::{Point, Waveform},
};
use num_complex::Complex64 as C;
use std::sync::{
    Mutex,
    atomic::{AtomicUsize, Ordering},
};

#[derive(Default)]
struct DirectOnly {
    complex: AtomicUsize,
    preparations: AtomicUsize,
}
impl LinearSolver for DirectOnly {
    fn name(&self) -> &'static str {
        "qualification-direct-only"
    }
    fn solve(&self, _: &Matrix, _: &[f64]) -> SimResult<Vec<f64>> {
        panic!("Unexpected dense fallback")
    }
    fn solve_csc(&self, a: &CscMatrix, b: &[f64]) -> SimResult<Vec<f64>> {
        FaerSparseLu.solve_csc(a, b)
    }
    fn solve_complex_csc(&self, a: &SparseComplexSystem, b: &[C]) -> SimResult<Vec<C>> {
        self.complex.fetch_add(1, Ordering::Relaxed);
        FaerSparseLu.solve_complex_csc(a, b)
    }
    fn prepare_csc(&self, a: &CscMatrix) -> SimResult<Option<PreparedSparseReal>> {
        self.preparations.fetch_add(1, Ordering::Relaxed);
        FaerSparseLu.prepare_csc(a)
    }
}

fn ladder(count: usize, scale: f64) -> (circuit_simulator::circuit::Circuit, Vec<f64>, Vec<f64>) {
    let mut deck = String::from("ladder\nV1 in 0 1\n");
    let mut r = Vec::new();
    let mut c = Vec::new();
    for i in 0..count {
        r.push(scale * (100. + 37. * i as f64));
        c.push(1e-6 * (1. + i as f64 / 3.) / scale);
        let before = if i == 0 {
            "in".to_string()
        } else {
            format!("n{}", i - 1)
        };
        deck.push_str(&format!(
            "R{i} {before} n{i} {}\nC{i} n{i} 0 {}\n",
            r[i], c[i]
        ));
    }
    deck.push_str(&format!("RL n{} 0 {}\n.end", count - 1, 1000. * scale));
    (parse(&deck).unwrap(), r, c)
}

fn qualify_ladder(count: usize, scale: f64) {
    let (circuit, r, c) = ladder(count, scale);
    let plan = compile(&circuit).unwrap();
    let source = C::new(-0.75, 1.25);
    let spy = DirectOnly::default();
    let phasor = [AcSourcePhasor {
        id: "V1".into(),
        real: source.re,
        imag: source.im,
    }];
    for provider in [&DenseLu as &dyn LinearSolver, &spy] {
        for sample in
            ac_with_source_phasors(&plan, &[0.1, 10., 1000., 100000.], &phasor, provider).unwrap()
        {
            // Independent impedance recursion from the load toward the input.
            let w = std::f64::consts::TAU * sample.frequency_hz;
            let mut z = vec![C::new(0., 0.); count];
            for i in (0..count).rev() {
                let load = if i + 1 == count {
                    C::new(1000. * scale, 0.)
                } else {
                    r[i + 1] + z[i + 1]
                };
                z[i] = C::new(1., 0.) / (C::new(0., w * c[i]) + C::new(1., 0.) / load);
            }
            let mut expected = source;
            for i in 0..count {
                expected *= z[i] / (r[i] + z[i]);
                let actual = &sample.values[i + 1];
                assert!(
                    (C::new(actual.real, actual.imag) - expected).norm()
                        < 2e-10 * (1. + expected.norm())
                );
            }
            let actual = &sample.values[plan.branch_index("V1").unwrap()];
            let current = -source / (r[0] + z[0]);
            assert!(
                (C::new(actual.real, actual.imag) - current).norm() < 2e-10 * (1. + current.norm())
            );
        }
    }
    assert_eq!(spy.complex.load(Ordering::Relaxed), 4);
}

#[test]
fn rc_ladders_match_continued_fraction_without_dense_fallback() {
    for count in 1..=8 {
        for scale in [0.01, 1., 100.] {
            qualify_ladder(count, scale)
        }
    }
}

#[test]
fn direct_series_rlc_matches_complex_impedance_and_flux_polarity() {
    let plan = compile(
        &parse("RLC\nV1 in 0 1\nR1 in mid 100\nL1 mid out 10m\nC1 out 0 1u\n.end").unwrap(),
    )
    .unwrap();
    let source = C::new(1., -2.);
    let spy = DirectOnly::default();
    for sample in ac_with_source_phasors(
        &plan,
        &[1., 100., 1591.5494309189535, 100000.],
        &[AcSourcePhasor {
            id: "V1".into(),
            real: source.re,
            imag: source.im,
        }],
        &spy,
    )
    .unwrap()
    {
        let w = std::f64::consts::TAU * sample.frequency_hz;
        let zc = C::new(0., -1. / (w * 1e-6));
        let zl = C::new(0., w * 0.01);
        let current = source / (100. + zl + zc);
        for (index, expected) in [
            (0, source),
            (1, current * (zl + zc)),
            (2, current * zc),
            (plan.branch_index("V1").unwrap(), -current),
            (plan.branch_index("L1").unwrap(), current),
        ] {
            let actual = &sample.values[index];
            assert!(
                (C::new(actual.real, actual.imag) - expected).norm()
                    < 2e-10 * (1. + expected.norm())
            );
        }
    }
    assert_eq!(spy.complex.load(Ordering::Relaxed), 4);
}

proptest::proptest! {
    #![proptest_config(proptest::test_runner::Config {cases:32,
        rng_seed:proptest::test_runner::RngSeed::Fixed(2026103004), ..Default::default()})]
    #[test]
    fn seeded_ladder_scale_property(count in 1_usize..9, exponent in -3_f64..3.) {
        qualify_ladder(count,10_f64.powf(exponent));
    }
}

fn transient_config(method: Method, breakpoint: bool) -> TransientConfig {
    let h = 1. / 1024.;
    TransientConfig {
        method,
        stop_s: 16. * h,
        max_step_s: h,
        max_steps: 32,
        initial_state: Some(vec![1., 0., -0.001]),
        sources: if breakpoint {
            vec![SourceOverride {
                id: "V1".into(),
                waveform: Waveform::Pwl {
                    points: vec![
                        Point {
                            time_s: 0.,
                            value: 1.,
                        },
                        Point {
                            time_s: 1.5 * h,
                            value: 1.,
                        },
                        Point {
                            time_s: 16. * h,
                            value: 2.,
                        },
                    ],
                },
            }]
        } else {
            vec![]
        },
        adaptive: None,
    }
}

#[test]
fn fixed_and_breakpoint_steps_reuse_only_matching_numeric_factors() {
    let plan =
        compile(&parse("RC\nV1 in 0 1\nR1 in out 1000\nC1 out 0 1u\n.end").unwrap()).unwrap();
    for method in [Method::BackwardEuler, Method::Trapezoidal] {
        for breakpoint in [false, true] {
            let config = transient_config(method, breakpoint);
            let spy = DirectOnly::default();
            let sparse = run(&plan, &spy, &config, &false.into()).unwrap();
            let dense = run(&plan, &DenseLu, &config, &false.into()).unwrap();
            assert_eq!(sparse.samples.len(), dense.samples.len());
            // Explicit initial state contributes no initialization solve; every
            // accepted fixed interval requires exactly one actual provider solve.
            assert_eq!(sparse.report.linear_solves, sparse.samples.len() - 1);
            assert_eq!(sparse.report.accepted_steps, sparse.samples.len() - 1);
            for (a, b) in sparse.samples.iter().zip(&dense.samples) {
                assert_eq!(a.time_s, b.time_s);
                for (x, y) in a.values.iter().zip(&b.values) {
                    assert!((x - y).abs() < 1e-10)
                }
            }
            let factors = if breakpoint { 2 } else { 1 };
            assert_eq!(sparse.report.prepared_factors, factors);
            assert_eq!(spy.preparations.load(Ordering::Relaxed), factors);
            assert_eq!(
                sparse.report.prepared_reuses + sparse.report.prepared_factors,
                sparse.report.accepted_steps
            );
        }
    }
}

struct StaleFactor(Mutex<Option<CscMatrix>>);
impl LinearSolver for StaleFactor {
    fn name(&self) -> &'static str {
        "deliberately-stale-factor"
    }
    fn solve(&self, _: &Matrix, _: &[f64]) -> SimResult<Vec<f64>> {
        panic!("Unexpected fallback")
    }
    fn prepare_csc(&self, a: &CscMatrix) -> SimResult<Option<PreparedSparseReal>> {
        let mut first = self.0.lock().unwrap();
        let old = first.get_or_insert_with(|| a.clone()).clone();
        let prepared = FaerSparseLu.prepare_csc(&old)?.unwrap();
        // Lie about the matrix identity: the owning driver must verify A*x=b.
        Ok(Some(PreparedSparseReal::new(a.clone(), move |b| {
            prepared.solve(b)
        })))
    }
}

#[test]
fn stale_factor_after_breakpoint_is_rejected_by_owned_residual_check() {
    let plan =
        compile(&parse("RC\nV1 in 0 1\nR1 in out 1000\nC1 out 0 1u\n.end").unwrap()).unwrap();
    for method in [Method::BackwardEuler, Method::Trapezoidal] {
        let error = run(
            &plan,
            &StaleFactor(Mutex::new(None)),
            &transient_config(method, true),
            &false.into(),
        )
        .err()
        .unwrap();
        assert_eq!(error.code, ErrorCode::NumericalFailure);
    }
}

struct FaultyComplex(u8);
impl LinearSolver for FaultyComplex {
    fn name(&self) -> &'static str {
        "faulty-complex"
    }
    fn solve(&self, _: &Matrix, _: &[f64]) -> SimResult<Vec<f64>> {
        panic!("Provider error hidden by fallback")
    }
    fn solve_complex_csc(&self, _: &SparseComplexSystem, b: &[C]) -> SimResult<Vec<C>> {
        match self.0 {
            0 => Ok(vec![]),
            1 => Ok(vec![C::new(f64::NAN, 0.); b.len()]),
            2 => Ok(vec![C::new(0., 0.); b.len()]),
            _ => Err(SimError::new(ErrorCode::NumericalFailure, "vendor failure")),
        }
    }
}

#[test]
fn faulty_and_singular_complex_circuits_fail_without_hidden_fallback() {
    let regular = compile(&parse("R\nV1 out 0 1\nR1 out 0 1000\n.end").unwrap()).unwrap();
    for mode in 0..4 {
        assert_eq!(
            ac_with_source_phasors(&regular, &[1.], &[], &FaultyComplex(mode))
                .unwrap_err()
                .code,
            ErrorCode::NumericalFailure
        )
    }
    let singular =
        compile(&parse("singular\nV1 out 0 1\nV2 out 0 1\nR1 out 0 1000\n.end").unwrap()).unwrap();
    for provider in [&DenseLu as &dyn LinearSolver, &FaerSparseLu] {
        assert_eq!(
            ac_with_source_phasors(&singular, &[1.], &[], provider)
                .unwrap_err()
                .code,
            ErrorCode::SingularMatrix
        );
    }
}
