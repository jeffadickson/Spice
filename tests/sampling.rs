use circuit_simulator::sampling::{SeededSequence, UniformRange};

#[test]
fn seeded_stream_is_reproducible_and_bounded() {
    let range = UniformRange { low: -2., high: 3. };
    let mut a = SeededSequence::new(42);
    let mut b = SeededSequence::new(42);
    for _ in 0..32 {
        let left = a.uniform(range).unwrap();
        assert_eq!(left, b.uniform(range).unwrap());
        assert!((-2. .. 3.).contains(&left));
    }
}

#[test]
fn malformed_sampling_ranges_fail_before_sampling() {
    assert!(UniformRange { low: 1., high: 1. }.validate().is_err());
    assert!(UniformRange { low: f64::NAN, high: 1. }.validate().is_err());
}
