use circuit_simulator::{
    api::{Analysis, Request, run},
    circuit::{Circuit, Element},
    ids::NodeId as N,
};
fn next(state: &mut u64) -> f64 {
    *state = state.wrapping_mul(6364136223846793005).wrapping_add(1);
    ((*state >> 11) as f64) / (1u64 << 53) as f64
}
#[test]
fn seeded_random_dividers_match_analytic_voltage_and_current() {
    let mut seed = 0x51_4d_46_4f_52_47_45_u64;
    for _ in 0..500 {
        let r1 = 1.0 + next(&mut seed) * 1e5;
        let r2 = 1.0 + next(&mut seed) * 1e5;
        let v = -50.0 + next(&mut seed) * 100.0;
        let circuit = Circuit {
            nodes: vec!["0".into(), "a".into(), "b".into()],
            elements: vec![
                Element::Voltage {
                    id: "V".into(),
                    p: N(1),
                    n: N(0),
                    volts: v,
                },
                Element::Resistor {
                    id: "R1".into(),
                    p: N(1),
                    n: N(2),
                    ohms: r1,
                },
                Element::Resistor {
                    id: "R2".into(),
                    p: N(2),
                    n: N(0),
                    ohms: r2,
                },
            ],
        };
        let response = run(&Request {
            schema_version: "1".into(),
            case_id: "random".into(),
            circuit,
            analysis: Analysis::OperatingPoint,
            backend: "lu".into(),
            experiment: None,
        })
        .unwrap();
        assert!((response.samples[0].values[1] - v * r2 / (r1 + r2)).abs() < 1e-8);
        assert!((response.samples[0].values[2] + v / (r1 + r2)).abs() < 1e-10);
    }
}
