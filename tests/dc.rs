use circuit_simulator::{
    backend::DenseLu,
    circuit::{Circuit, Element},
    compile::compile,
    dc::operating_point,
    error::ErrorCode,
    ids::NodeId as N,
    linalg::{LinearSolver, Matrix},
};
fn divider() -> Circuit {
    Circuit {
        nodes: vec!["0".into(), "in".into(), "out".into()],
        elements: vec![
            Element::Voltage {
                id: "V1".into(),
                p: N(1),
                n: N(0),
                volts: 10.0,
            },
            Element::Resistor {
                id: "R1".into(),
                p: N(1),
                n: N(2),
                ohms: 1000.0,
            },
            Element::Resistor {
                id: "R2".into(),
                p: N(2),
                n: N(0),
                ohms: 1000.0,
            },
        ],
    }
}
#[test]
fn divider_voltage_and_source_current() {
    let x = operating_point(&compile(&divider()).unwrap(), &DenseLu).unwrap();
    assert!((x[1] - 5.0).abs() < 1e-12);
    assert!((x[2] + 0.005).abs() < 1e-12);
}
#[test]
fn invalid_duplicate_names() {
    let mut c = divider();
    c.nodes[2] = "in".into();
    assert_eq!(compile(&c).unwrap_err().code, ErrorCode::InvalidInput);
}
#[test]
fn negative_resistance_rejected() {
    let mut c = divider();
    c.elements.push(Element::Resistor {
        id: "bad".into(),
        p: N(1),
        n: N(0),
        ohms: -1.0,
    });
    assert!(compile(&c).is_err());
}
#[test]
fn floating_component_is_singular() {
    let c = Circuit {
        nodes: vec!["0".into(), "a".into(), "b".into()],
        elements: vec![Element::Resistor {
            id: "R".into(),
            p: N(1),
            n: N(2),
            ohms: 10.0,
        }],
    };
    assert_eq!(
        operating_point(&compile(&c).unwrap(), &DenseLu)
            .unwrap_err()
            .code,
        ErrorCode::SingularMatrix
    );
}
#[test]
fn malformed_matrix_rejected() {
    let a = Matrix {
        n: 2,
        values: vec![0.0],
    };
    assert_eq!(
        DenseLu.solve(&a, &[0.0, 0.0]).unwrap_err().code,
        ErrorCode::InvalidInput
    );
}
#[test]
fn nan_rhs_rejected() {
    let a = Matrix {
        n: 1,
        values: vec![1.0],
    };
    assert!(DenseLu.solve(&a, &[f64::NAN]).is_err());
}
