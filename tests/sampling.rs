use circuit_simulator::sampling::{SeededSequence, UniformRange, uniform_values};

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
fn generated_sample_vectors_retain_seeded_order() {
    let range = UniformRange { low: 10., high: 20. };
    assert_eq!(uniform_values(9, 3, range).unwrap(), uniform_values(9, 3, range).unwrap());
    assert!(uniform_values(9, 0, range).is_err());
    assert!(uniform_values(9, 65, range).is_err());
}

#[test]
fn malformed_sampling_ranges_fail_before_sampling() {
    assert!(UniformRange { low: 1., high: 1. }.validate().is_err());
    assert!(UniformRange { low: f64::NAN, high: 1. }.validate().is_err());
}
