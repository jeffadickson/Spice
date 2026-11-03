//! Frozen public-API linearization isolates the provider from the newer BJT
//! model IR. The fixture records bias, source request hash and 10 MHz frequency.
use circuit_simulator::{
    ac::verify_complex_system,
    backend::DenseLu,
    backend_faer::FaerSparseLu,
    solver_contract::{SparseComplexSystem, SparseLinearBackend},
    sparse::CscPattern,
};
use num_complex::Complex64;
use serde::Deserialize;
#[derive(Deserialize)]
struct FrozenSystem {
    dimension: usize,
    entries: Vec<(usize, usize, f64, f64)>,
    rhs: Vec<(f64, f64)>,
}
fn fixture() -> (SparseComplexSystem, Vec<Complex64>) {
    let data: FrozenSystem =
        serde_json::from_str(include_str!("fixtures/bjt-faer-ac-frozen.json")).unwrap();
    let mut pointers = vec![0; data.dimension + 1];
    for &(_, column, _, _) in &data.entries {
        pointers[column + 1] += 1;
    }
    for column in 1..=data.dimension {
        pointers[column] += pointers[column - 1];
    }
    let pattern = CscPattern::new(
        data.dimension,
        pointers,
        data.entries.iter().map(|e| e.0).collect(),
    )
    .unwrap();
    let system = SparseComplexSystem::new(
        pattern,
        data.entries
            .iter()
            .map(|e| Complex64::new(e.2, e.3))
            .collect(),
    )
    .unwrap();
    (
        system,
        data.rhs.iter().map(|v| Complex64::new(v.0, v.1)).collect(),
    )
}
#[test]
fn frozen_bjt_complex_system_meets_the_unchanged_componentwise_contract() {
    let (system, rhs) = fixture();
    for (name, solver) in [
        ("lu", &DenseLu as &dyn SparseLinearBackend),
        ("faer", &FaerSparseLu),
    ] {
        let values = solver.solve_sparse_complex_rhs(&system, &rhs).unwrap();
        if let Err(error) = verify_complex_system(&system, &rhs, &values) {
            let mut sums = vec![Complex64::new(0., 0.); rhs.len()];
            let mut scales: Vec<_> = rhs.iter().map(|v| v.norm()).collect();
            for ((row, column), coefficient) in system.pattern().slots().zip(system.values()) {
                let term = coefficient * values[column];
                sums[row] += term;
                scales[row] += term.norm();
            }
            eprintln!(
                "{name}: {error:?}; rhs={rhs:?}; values={values:?}; sums={sums:?}; scales={scales:?}"
            );
            panic!("{name} failed independent complex acceptance");
        }
    }
}

#[test]
fn canonical_complex_error_is_a_score_not_an_acceptance_verdict() {
    use circuit_simulator::solver_contract::{complex_backward_error, verify_complex_solution};
    let system = SparseComplexSystem::new(
        CscPattern::new(1, vec![0, 1], vec![0]).unwrap(),
        vec![Complex64::new(1., 0.)],
    )
    .unwrap();
    let rhs = [Complex64::new(0., 0.)];
    let wrong = [Complex64::new(0., f64::EPSILON)];
    assert_eq!(complex_backward_error(&system, &rhs, &wrong).unwrap(), 1.);
    assert!(verify_complex_solution(&system, &rhs, &wrong).is_err());
    assert_eq!(
        verify_complex_solution(&system, &rhs, &[Complex64::new(0., 0.)]).unwrap(),
        0.
    );
    for scale in [1e-120, 1e-9, 1e9, 1e120] {
        let system = SparseComplexSystem::new(
            CscPattern::new(1, vec![0, 1], vec![0]).unwrap(),
            vec![Complex64::new(scale, scale)],
        )
        .unwrap();
        let rhs = [Complex64::new(scale, scale)];
        assert!(verify_complex_solution(&system, &rhs, &[Complex64::new(1.01, 0.)]).is_err());
        assert!(verify_complex_solution(&system, &rhs, &[Complex64::new(1., 0.)]).is_ok());
    }
}

proptest::proptest! {
    #![proptest_config(proptest::test_runner::Config {
        cases: 128,
        rng_seed: proptest::test_runner::RngSeed::Fixed(57120261105),
        failure_persistence: Some(Box::new(proptest::test_runner::FileFailurePersistence::Direct(
            "tests/faer_complex_refinement.proptest-regressions"))),
        ..Default::default()
    })]
    #[test]
    fn genuine_complex_rhs_preserves_frozen_matrix_linearity(real in -10_i16..11,imag in -10_i16..11) {
        let(system,base_rhs)=fixture();
        let base=SparseLinearBackend::solve_sparse_complex_rhs(&DenseLu,&system,&base_rhs).unwrap();
        proptest::prop_assert!(verify_complex_system(&system,&base_rhs,&base).is_ok());
        let scale=Complex64::new(f64::from(real)/10.,f64::from(imag)/10.);
        let rhs:Vec<_>=base_rhs.iter().map(|v|*v*scale).collect();
        let actual=SparseLinearBackend::solve_sparse_complex_rhs(&FaerSparseLu,&system,&rhs).unwrap();
        proptest::prop_assert!(verify_complex_system(&system,&rhs,&actual).is_ok());
        for(value,reference) in actual.iter().zip(base) {
            let expected=reference*scale;
            proptest::prop_assert!((*value-expected).norm()<=1e-9*expected.norm().max(1e-30));
        }
    }
}
