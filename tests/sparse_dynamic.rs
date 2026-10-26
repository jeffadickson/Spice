//! Direct sparse dynamic assembly contracts, independent of provider layouts.
use circuit_simulator::{error::ErrorCode, sparse::CscMatrix};

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
