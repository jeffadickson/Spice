//! Independent boundary and difficult-topology evidence for bounded Newton paths.
use circuit_simulator::newton::norm_inf;

#[test]
fn nonfinite_norm_components_cannot_disappear_in_a_maximum() {
    for bad in [
        f64::NAN,
        f64::INFINITY,
        f64::NEG_INFINITY,
        f64::from_bits(0x7ff0_0000_0000_0001),
    ] {
        for values in [vec![bad], vec![bad, 1.], vec![1., bad]] {
            assert!(norm_inf(&values).is_err(), "accepted {values:?}");
        }
    }
    assert_eq!(norm_inf(&[]).unwrap(), 0.);
    assert_eq!(norm_inf(&[-0., 0., -3., 2.]).unwrap(), 3.);
}

#[test]
fn seeded_ieee_norm_boundaries_preserve_nonfinite_rejection() {
    // Replay seed 0x4e4557544f4e3335; raw IEEE patterns include subnormals,
    // infinities and NaNs without depending on a production random generator.
    let mut state = 0x4e45_5754_4f4e_3335_u64;
    for _ in 0..512 {
        state = state.wrapping_mul(6364136223846793005).wrapping_add(1);
        let value = f64::from_bits(state);
        let result = norm_inf(&[1., value, -2.]);
        if value.is_finite() {
            assert_eq!(result.unwrap(), value.abs().max(2.));
        } else {
            assert!(result.is_err(), "seed state {state:016x}");
        }
    }
}
