//! Direct sparse dynamic assembly contracts, independent of provider layouts.
use circuit_simulator::solver_contract::SparseComplexSystem;
use circuit_simulator::{error::ErrorCode, sparse::CscMatrix};
use num_complex::Complex64;

#[test]
fn complex_dynamic_union_preserves_signed_flux_and_conjugacy() {
    let g = CscMatrix::new(2, vec![0, 2, 3], vec![0, 1, 1], vec![2., -3., 4.]).unwrap();
    let q = CscMatrix::new(2, vec![0, 1, 3], vec![1, 0, 1], vec![5., 7., -11.]).unwrap();
    let forward = SparseComplexSystem::from_real_parts(&g, &q, 2.).unwrap();
    let reverse = SparseComplexSystem::from_real_parts(&g, &q, -2.).unwrap();
    assert_eq!(forward.pattern().col_ptr(), &[0, 2, 4]);
    assert_eq!(forward.pattern().rows(), &[0, 1, 0, 1]);
    assert_eq!(
        forward.values(),
        &[
            Complex64::new(2., 0.),
            Complex64::new(-3., 10.),
            Complex64::new(0., 14.),
            Complex64::new(4., -22.)
        ]
    );
    assert!(
        forward
            .values()
            .iter()
            .zip(reverse.values())
            .all(|(a, b)| *a == b.conj())
    );
    forward
        .validate_complex_rhs(&[Complex64::new(1., 2.), Complex64::new(3., -4.)])
        .unwrap();
    assert!(forward.validate_complex_rhs(&[]).is_err());
    for value in [
        Complex64::new(f64::NAN, 0.),
        Complex64::new(0., f64::INFINITY),
    ] {
        assert!(
            forward
                .validate_complex_rhs(&[value, Complex64::new(0., 0.)])
                .is_err()
        );
    }
}

#[test]
fn complex_dynamic_overflow_and_nonfinite_frequency_scales_are_rejected() {
    let huge = CscMatrix::new(1, vec![0, 1], vec![0], vec![f64::MAX]).unwrap();
    for scale in [f64::NAN, f64::INFINITY, 2.] {
        assert!(SparseComplexSystem::from_real_parts(&huge, &huge, scale).is_err());
    }
}

#[test]
fn canonical_union_retains_cancellation_and_empty_columns() {
    let left = CscMatrix::new(3, vec![0, 1, 1, 2], vec![0, 1], vec![2., 3.]).unwrap();
    let right = CscMatrix::new(3, vec![0, 1, 2, 3], vec![0, 2, 2], vec![2., 5., 7.]).unwrap();
    let combined = left.combine_scaled(&right, 1., -1.).unwrap();
    assert_eq!(combined.pattern().col_ptr(), &[0, 1, 2, 4]);
    assert_eq!(combined.pattern().rows(), &[0, 2, 1, 2]);
    assert_eq!(combined.values(), &[0., -5., 3., -7.]);
    let alternate = left.combine_scaled(&right, 2., 0.).unwrap();
    assert_eq!(combined.pattern(), alternate.pattern());
    assert_eq!(alternate.values(), &[4., 0., 6., 0.]);
}

#[test]
fn dynamic_combinations_reject_dimensions_nonfinite_scales_and_overflow() {
    let one = CscMatrix::new(1, vec![0, 1], vec![0], vec![f64::MAX]).unwrap();
    let empty = CscMatrix::new(2, vec![0, 0, 0], vec![], vec![]).unwrap();
    assert_eq!(
        one.combine_scaled(&empty, 1., 1.).unwrap_err().code,
        ErrorCode::InvalidInput
    );
    for bad in [f64::NAN, f64::INFINITY, f64::NEG_INFINITY] {
        assert!(one.combine_scaled(&one, bad, 0.).is_err());
        assert!(one.combine_scaled(&one, 0., bad).is_err());
    }
    assert!(one.combine_scaled(&one, 1., 1.).is_err());
}
