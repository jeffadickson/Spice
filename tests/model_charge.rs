//! Independent equation/unit/domain contracts for the bounded charge profile.
use circuit_simulator::charge::constant_capacitance;

#[test]
fn constant_charge_has_signed_charge_positive_derivative_and_even_energy() {
    for voltage in [-3., 0., 3.] {
        let value = constant_capacitance(2., voltage).unwrap();
        assert_eq!(value.charge_coulombs, 2. * voltage);
        assert_eq!(value.capacitance_farads, 2.);
        assert_eq!(value.energy_joules, voltage * voltage);
        let h = 1e-4;
        let derivative = (constant_capacitance(2., voltage + h)
            .unwrap()
            .charge_coulombs
            - constant_capacitance(2., voltage - h)
                .unwrap()
                .charge_coulombs)
            / (2. * h);
        assert!((derivative - value.capacitance_farads).abs() < 1e-10);
    }
}

#[test]
fn constant_charge_rejects_bad_domains_and_avoids_needless_energy_overflow() {
    for capacitance in [0., -1., f64::NAN, f64::INFINITY] {
        assert!(constant_capacitance(capacitance, 1.).is_err());
    }
    for voltage in [f64::NAN, f64::INFINITY, f64::NEG_INFINITY] {
        assert!(constant_capacitance(1., voltage).is_err());
    }
    assert!(constant_capacitance(f64::MAX, 2.).is_err());
    assert!(constant_capacitance(1., f64::MAX).is_err());
    let value = constant_capacitance(1e-300, 1e200).unwrap();
    assert!((value.charge_coulombs / 1e-100 - 1.).abs() < 1e-15);
    assert!((value.energy_joules / 5e99 - 1.).abs() < 1e-15);
}
