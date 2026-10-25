use circuit_simulator::{
    api::{Analysis, Request, run},
    backend::DenseLu,
    backend_faer::FaerSparseLu,
    dc::solve_verified,
    linalg::{LinearSolver, Matrix},
    netlist::parse,
    registry::BackendRegistry,
    sparse::CscMatrix,
};
#[test]
fn independent_providers_match_nonsymmetric_indefinite_system() {
    let a = Matrix {
        n: 3,
        values: vec![0., 2., 1., 3., -1., 0., 1., 0., 1.],
    };
    let b = [3., 2., 2.];
    for solver in [&DenseLu as &dyn LinearSolver, &FaerSparseLu] {
        let (x, error) = solve_verified(&a, &b, solver).unwrap();
        assert!(error < 1e-12);
        for value in x {
            assert!((value - 1.).abs() < 1e-12);
        }
    }
}
#[test]
fn sparse_and_dense_ac_agree() {
    let c = parse("title\nV1 in 0 1\nR1 in out 1k\nC1 out 0 1u").unwrap();
    let request = |backend: &str| Request {
        schema_version: "1".into(),
        case_id: "ac-providers".into(),
        circuit: c.clone(),
        analysis: Analysis::Ac {
            frequencies_hz: vec![1., 159.15494309189535, 1e5],
        },
        backend: backend.into(),
        experiment: None,
    };
    let a = run(&request("lu")).unwrap();
    let b = run(&request("faer")).unwrap();
    for (x, y) in a.ac_samples.iter().zip(&b.ac_samples) {
        for (x, y) in x.values.iter().zip(&y.values) {
            assert!((x.real - y.real).abs() < 1e-10);
            assert!((x.imag - y.imag).abs() < 1e-10);
        }
    }
}
#[test]
fn prepared_factors_own_original_numeric_values() {
    for solver in [&DenseLu as &dyn LinearSolver, &FaerSparseLu] {
        let mut a = Matrix {
            n: 1,
            values: vec![2.],
        };
        let prepared = solver.prepare(&a).unwrap().unwrap();
        a.values[0] = 4.;
        assert_eq!(prepared.solve(&[6.]).unwrap(), vec![3.]);
        assert_eq!(prepared.solve(&[8.]).unwrap(), vec![4.]);
        assert_eq!(solver.solve(&a, &[8.]).unwrap(), vec![2.]);
        assert!(prepared.solve(&[f64::NAN]).is_err());
        assert!(prepared.solve(&[]).is_err());
    }
}
#[test]
fn malformed_sparse_layouts_rejected() {
    for result in [
        CscMatrix::new(2, vec![0, 2, 1], vec![0], vec![1.]),
        CscMatrix::new(2, vec![0, 2, 2], vec![1, 1], vec![1., 2.]),
        CscMatrix::new(2, vec![0, 1, 1], vec![9], vec![1.]),
    ] {
        assert!(result.is_err());
    }
    let c = CscMatrix::from_dense(&Matrix {
        n: 2,
        values: vec![1., 0., 2., 3.],
    })
    .unwrap();
    assert_eq!(c.dimension(), 2);
    assert_eq!(c.nnz(), 3);
}
#[test]
fn capability_discovery_is_sorted_and_honest() {
    let c = BackendRegistry::reference().capabilities();
    assert_eq!(
        c.iter().map(|x| x.0.as_str()).collect::<Vec<_>>(),
        vec!["faer", "lu", "qr"]
    );
    assert!(c[0].1.sparse);
    assert!(c[1].1.complex);
    assert!(!c[2].1.complex);
    assert!(c.iter().all(|x| !x.1.gpu));
}
