use circuit_simulator::{
    api::{Request, run},
    backend::{DenseLu, DenseQr},
    circuit::{Circuit, Element},
    compile::compile,
    dc::operating_point,
    error::ErrorCode,
    ids::NodeId as N,
    transient::{TransientOptions, transient, transient_controlled},
};
pub fn rc() -> Circuit {
    Circuit {
        nodes: vec!["0".into(), "in".into(), "out".into()],
        elements: vec![
            Element::Voltage {
                id: "V".into(),
                p: N(1),
                n: N(0),
                volts: 1.0,
            },
            Element::Resistor {
                id: "R".into(),
                p: N(1),
                n: N(2),
                ohms: 1000.0,
            },
            Element::Capacitor {
                id: "C".into(),
                p: N(2),
                n: N(0),
                farads: 1e-6,
            },
        ],
    }
}
#[test]
fn rc_step_matches_analytic_envelope() {
    let samples = transient(
        &compile(&rc()).unwrap(),
        &DenseLu,
        &TransientOptions {
            stop_s: 0.005,
            step_s: 1e-6,
            max_steps: 6000,
        },
    )
    .unwrap();
    assert_eq!(samples.len(), 5000);
    assert!((samples.last().unwrap().values[1] - (1.0 - (-5.0_f64).exp())).abs() < 2e-5);
}
#[test]
fn qr_and_lu_agree() {
    let p = compile(&rc()).unwrap();
    let a = operating_point(&p, &DenseLu).unwrap();
    let b = operating_point(&p, &DenseQr).unwrap();
    for (x, y) in a.iter().zip(b) {
        assert!((x - y).abs() < 1e-10);
    }
}
#[test]
fn cancellation_and_budget_are_classified() {
    let p = compile(&rc()).unwrap();
    let o = TransientOptions {
        stop_s: 1.0,
        step_s: 0.01,
        max_steps: 200,
    };
    assert_eq!(
        transient_controlled(&p, &DenseLu, &o, &true.into())
            .unwrap_err()
            .code,
        ErrorCode::Cancelled
    );
    let o = TransientOptions { max_steps: 1, ..o };
    assert_eq!(
        transient(&p, &DenseLu, &o).unwrap_err().code,
        ErrorCode::ResourceExhausted
    );
}
#[test]
fn request_round_trip_and_manifest() {
    let request:Request=serde_json::from_value(serde_json::json!({"schema_version":"1","case_id":"rc","circuit":rc(),"analysis":{"kind":"operating_point"}})).unwrap();
    let result = run(&request).unwrap();
    assert!(result.complete);
    assert_eq!(result.signals[1].unit, "V");
    assert_eq!(result.samples[0].values[1], 1.0);
}
