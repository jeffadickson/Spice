use circuit_simulator::api::{Request, run};
use circuit_simulator::variation::{ScalarKind, replace_scalar, scalar_kind};
use circuit_simulator::{
    backend::DenseLu,
    circuit::{Circuit, Element},
    compile::compile,
    dc::operating_point,
    ids::NodeId,
    power::dc_power,
};

#[test]
fn vccs_stamps_controlled_current_with_declared_polarity() {
    let circuit = Circuit {
        nodes: vec!["0".into(), "in".into(), "out".into()],
        elements: vec![
            Element::Voltage {
                id: "V1".into(),
                p: NodeId(1),
                n: NodeId(0),
                volts: 2.,
            },
            Element::Vccs {
                id: "G1".into(),
                p: NodeId(2),
                n: NodeId(0),
                cp: NodeId(1),
                cn: NodeId(0),
                siemens: 0.5,
            },
            Element::Resistor {
                id: "R1".into(),
                p: NodeId(2),
                n: NodeId(0),
                ohms: 1.,
            },
        ],
    };
    let x = operating_point(&compile(&circuit).unwrap(), &DenseLu).unwrap();
    assert!((x[1] + 1.).abs() < 1e-12);
}

#[test]
fn vccs_negative_control_voltage_reverses_output_polarity() {
    let circuit = Circuit {
        nodes: vec!["0".into(), "in".into(), "out".into()],
        elements: vec![
            Element::Voltage {
                id: "V1".into(),
                p: NodeId(1),
                n: NodeId(0),
                volts: -2.,
            },
            Element::Vccs {
                id: "G1".into(),
                p: NodeId(2),
                n: NodeId(0),
                cp: NodeId(1),
                cn: NodeId(0),
                siemens: 0.5,
            },
            Element::Resistor {
                id: "R1".into(),
                p: NodeId(2),
                n: NodeId(0),
                ohms: 1.,
            },
        ],
    };
    let x = operating_point(&compile(&circuit).unwrap(), &DenseLu).unwrap();
    assert!((x[1] - 1.).abs() < 1e-12);
}

#[test]
fn vccs_rejects_identical_control_terminals() {
    let circuit = Circuit {
        nodes: vec!["0".into(), "out".into()],
        elements: vec![Element::Vccs {
            id: "G1".into(),
            p: NodeId(1),
            n: NodeId(0),
            cp: NodeId(0),
            cn: NodeId(0),
            siemens: 1.,
        }],
    };
    assert!(compile(&circuit).is_err());
}

#[test]
fn vccs_power_accounting_balances_the_controlling_supply() {
    let circuit = Circuit {
        nodes: vec!["0".into(), "in".into(), "out".into()],
        elements: vec![
            Element::Voltage {
                id: "V1".into(),
                p: NodeId(1),
                n: NodeId(0),
                volts: 2.,
            },
            Element::Vccs {
                id: "G1".into(),
                p: NodeId(2),
                n: NodeId(0),
                cp: NodeId(1),
                cn: NodeId(0),
                siemens: 0.5,
            },
            Element::Resistor {
                id: "R1".into(),
                p: NodeId(2),
                n: NodeId(0),
                ohms: 1.,
            },
        ],
    };
    let plan = compile(&circuit).unwrap();
    let x = operating_point(&plan, &DenseLu).unwrap();
    let report = dc_power(&plan, &x).unwrap();
    assert!(report.sum_absorbed_w.abs() < 1e-12);
}

#[test]
fn vccs_json_request_is_solved_through_the_public_api() {
    let request: Request = serde_json::from_str(
        r#"{
      "schema_version":"1","case_id":"vccs-json","circuit":{"nodes":["0","in","out"],"elements":[
        {"kind":"voltage","id":"V1","p":1,"n":0,"volts":2.0},
        {"kind":"vccs","id":"G1","p":2,"n":0,"cp":1,"cn":0,"siemens":0.5},
        {"kind":"resistor","id":"R1","p":2,"n":0,"ohms":1.0}
      ]},"analysis":{"kind":"operating_point"}}"#,
    )
    .unwrap();
    let result = run(&request).unwrap();
    assert!((result.samples[0].values[1] + 1.).abs() < 1e-12);
}

#[test]
fn vcvs_constrains_output_voltage_with_controlled_polarity() {
    let circuit = Circuit {
        nodes: vec!["0".into(), "in".into(), "out".into()],
        elements: vec![
            Element::Voltage {
                id: "V1".into(),
                p: NodeId(1),
                n: NodeId(0),
                volts: 2.,
            },
            Element::Vcvs {
                id: "E1".into(),
                p: NodeId(2),
                n: NodeId(0),
                cp: NodeId(1),
                cn: NodeId(0),
                gain: -3.,
            },
            Element::Resistor {
                id: "R1".into(),
                p: NodeId(2),
                n: NodeId(0),
                ohms: 1.,
            },
        ],
    };
    let x = operating_point(&compile(&circuit).unwrap(), &DenseLu).unwrap();
    assert!((x[1] + 6.).abs() < 1e-12);
}

#[test]
fn vcvs_rejects_identical_control_terminals() {
    let circuit = Circuit {
        nodes: vec!["0".into(), "out".into()],
        elements: vec![Element::Vcvs {
            id: "E1".into(),
            p: NodeId(1),
            n: NodeId(0),
            cp: NodeId(0),
            cn: NodeId(0),
            gain: 1.,
        }],
    };
    assert!(compile(&circuit).is_err());
}

#[test]
fn vcvs_power_accounting_balances_the_controlling_supply() {
    let circuit = Circuit {
        nodes: vec!["0".into(), "in".into(), "out".into()],
        elements: vec![
            Element::Voltage {
                id: "V1".into(),
                p: NodeId(1),
                n: NodeId(0),
                volts: 2.,
            },
            Element::Vcvs {
                id: "E1".into(),
                p: NodeId(2),
                n: NodeId(0),
                cp: NodeId(1),
                cn: NodeId(0),
                gain: -3.,
            },
            Element::Resistor {
                id: "R1".into(),
                p: NodeId(2),
                n: NodeId(0),
                ohms: 1.,
            },
        ],
    };
    let plan = compile(&circuit).unwrap();
    let x = operating_point(&plan, &DenseLu).unwrap();
    assert!(dc_power(&plan, &x).unwrap().sum_absorbed_w.abs() < 1e-12);
}

#[test]
fn vcvs_gain_variation_is_immutable_and_dimensionless() {
    let circuit = Circuit {
        nodes: vec!["0".into(), "in".into(), "out".into()],
        elements: vec![
            Element::Voltage {
                id: "V1".into(),
                p: NodeId(1),
                n: NodeId(0),
                volts: 2.,
            },
            Element::Vcvs {
                id: "E1".into(),
                p: NodeId(2),
                n: NodeId(0),
                cp: NodeId(1),
                cn: NodeId(0),
                gain: 1.,
            },
            Element::Resistor {
                id: "R1".into(),
                p: NodeId(2),
                n: NodeId(0),
                ohms: 1.,
            },
        ],
    };
    assert_eq!(
        scalar_kind(&circuit, "E1").unwrap(),
        ScalarKind::VoltageGain
    );
    let varied = replace_scalar(&circuit, "E1", -3.).unwrap();
    let x = operating_point(&compile(&varied).unwrap(), &DenseLu).unwrap();
    assert!((x[1] + 6.).abs() < 1e-12);
    assert_eq!(
        scalar_kind(&circuit, "E1").unwrap(),
        ScalarKind::VoltageGain
    );
}

#[test]
fn vcvs_json_request_is_solved_through_the_public_api() {
    let request: Request = serde_json::from_str(
        r#"{
      "schema_version":"1","case_id":"vcvs-json","circuit":{"nodes":["0","in","out"],"elements":[
        {"kind":"voltage","id":"V1","p":1,"n":0,"volts":2.0},
        {"kind":"vcvs","id":"E1","p":2,"n":0,"cp":1,"cn":0,"gain":-3.0},
        {"kind":"resistor","id":"R1","p":2,"n":0,"ohms":1.0}
      ]},"analysis":{"kind":"operating_point"}}"#,
    )
    .unwrap();
    let result = run(&request).unwrap();
    assert!((result.samples[0].values[1] + 6.).abs() < 1e-12);
    assert_eq!(result.signals[3].id, "i(E1)");
    assert!((result.samples[0].values[3] - 6.).abs() < 1e-12);
}

#[test]
fn cccs_stamps_named_control_branch_with_declared_polarity() {
    let circuit = Circuit {
        nodes: vec!["0".into(), "in".into(), "out".into()],
        elements: vec![
            Element::Voltage {
                id: "V1".into(),
                p: NodeId(1),
                n: NodeId(0),
                volts: 2.,
            },
            Element::Resistor {
                id: "RCONTROL".into(),
                p: NodeId(1),
                n: NodeId(0),
                ohms: 1.,
            },
            Element::Cccs {
                id: "F1".into(),
                p: NodeId(2),
                n: NodeId(0),
                control_id: "V1".into(),
                gain: 0.5,
            },
            Element::Resistor {
                id: "ROUT".into(),
                p: NodeId(2),
                n: NodeId(0),
                ohms: 1.,
            },
        ],
    };
    let x = operating_point(&compile(&circuit).unwrap(), &DenseLu).unwrap();
    // i(V1) is -2 A for a source delivering current; F1 therefore injects -1 A.
    assert!((x[1] - 1.).abs() < 1e-12);
}

#[test]
fn cccs_rejects_unknown_or_nonbranch_control_ids() {
    let circuit = Circuit {
        nodes: vec!["0".into(), "out".into()],
        elements: vec![
            Element::Cccs {
                id: "F1".into(),
                p: NodeId(1),
                n: NodeId(0),
                control_id: "R1".into(),
                gain: 1.,
            },
            Element::Resistor {
                id: "R1".into(),
                p: NodeId(1),
                n: NodeId(0),
                ohms: 1.,
            },
        ],
    };
    assert!(compile(&circuit).is_err());
}

#[test]
fn cccs_gain_variation_is_immutable_and_dimensionless() {
    let circuit = Circuit {
        nodes: vec!["0".into(), "in".into(), "out".into()],
        elements: vec![
            Element::Voltage {
                id: "V1".into(),
                p: NodeId(1),
                n: NodeId(0),
                volts: 2.,
            },
            Element::Resistor {
                id: "RCONTROL".into(),
                p: NodeId(1),
                n: NodeId(0),
                ohms: 1.,
            },
            Element::Cccs {
                id: "F1".into(),
                p: NodeId(2),
                n: NodeId(0),
                control_id: "V1".into(),
                gain: 0.5,
            },
            Element::Resistor {
                id: "ROUT".into(),
                p: NodeId(2),
                n: NodeId(0),
                ohms: 1.,
            },
        ],
    };
    assert_eq!(
        scalar_kind(&circuit, "F1").unwrap(),
        ScalarKind::CurrentGain
    );
    let varied = replace_scalar(&circuit, "F1", -1.).unwrap();
    let x = operating_point(&compile(&varied).unwrap(), &DenseLu).unwrap();
    assert!((x[1] + 2.).abs() < 1e-12);
    assert_eq!(
        scalar_kind(&circuit, "F1").unwrap(),
        ScalarKind::CurrentGain
    );
}

#[test]
fn cccs_power_accounting_balances_the_control_supply() {
    let circuit = Circuit {
        nodes: vec!["0".into(), "in".into(), "out".into()],
        elements: vec![
            Element::Voltage {
                id: "V1".into(),
                p: NodeId(1),
                n: NodeId(0),
                volts: 2.,
            },
            Element::Resistor {
                id: "RCONTROL".into(),
                p: NodeId(1),
                n: NodeId(0),
                ohms: 1.,
            },
            Element::Cccs {
                id: "F1".into(),
                p: NodeId(2),
                n: NodeId(0),
                control_id: "V1".into(),
                gain: 0.5,
            },
            Element::Resistor {
                id: "ROUT".into(),
                p: NodeId(2),
                n: NodeId(0),
                ohms: 1.,
            },
        ],
    };
    let plan = compile(&circuit).unwrap();
    let x = operating_point(&plan, &DenseLu).unwrap();
    assert!(dc_power(&plan, &x).unwrap().sum_absorbed_w.abs() < 1e-12);
}

#[test]
fn cccs_json_request_is_solved_through_the_public_api() {
    let request: Request = serde_json::from_str(
        r#"{
      "schema_version":"1","case_id":"cccs-json","circuit":{"nodes":["0","in","out"],"elements":[
        {"kind":"voltage","id":"V1","p":1,"n":0,"volts":2.0},
        {"kind":"resistor","id":"RCONTROL","p":1,"n":0,"ohms":1.0},
        {"kind":"cccs","id":"F1","p":2,"n":0,"control_id":"V1","gain":0.5},
        {"kind":"resistor","id":"ROUT","p":2,"n":0,"ohms":1.0}
      ]},"analysis":{"kind":"operating_point"}}"#,
    )
    .unwrap();
    let result = run(&request).unwrap();
    assert!((result.samples[0].values[1] - 1.).abs() < 1e-12);
}

#[test]
fn ccvs_stamps_named_control_branch_with_declared_polarity() {
    let circuit = Circuit {
        nodes: vec!["0".into(), "in".into(), "out".into()],
        elements: vec![
            Element::Voltage {
                id: "V1".into(),
                p: NodeId(1),
                n: NodeId(0),
                volts: 2.,
            },
            Element::Resistor {
                id: "RCONTROL".into(),
                p: NodeId(1),
                n: NodeId(0),
                ohms: 1.,
            },
            Element::Ccvs {
                id: "H1".into(),
                p: NodeId(2),
                n: NodeId(0),
                control_id: "V1".into(),
                ohms: 3.,
            },
            Element::Resistor {
                id: "ROUT".into(),
                p: NodeId(2),
                n: NodeId(0),
                ohms: 1.,
            },
        ],
    };
    let x = operating_point(&compile(&circuit).unwrap(), &DenseLu).unwrap();
    // i(V1) is -2 A, so H1's output voltage is -6 V.
    assert!((x[1] + 6.).abs() < 1e-12);
}

#[test]
fn ccvs_rejects_unknown_or_nonbranch_control_ids() {
    let circuit = Circuit {
        nodes: vec!["0".into(), "out".into()],
        elements: vec![
            Element::Ccvs {
                id: "H1".into(),
                p: NodeId(1),
                n: NodeId(0),
                control_id: "R1".into(),
                ohms: 1.,
            },
            Element::Resistor {
                id: "R1".into(),
                p: NodeId(1),
                n: NodeId(0),
                ohms: 1.,
            },
        ],
    };
    assert!(compile(&circuit).is_err());
}

#[test]
fn ccvs_transresistance_variation_is_immutable() {
    let circuit = Circuit {
        nodes: vec!["0".into(), "in".into(), "out".into()],
        elements: vec![
            Element::Voltage {
                id: "V1".into(),
                p: NodeId(1),
                n: NodeId(0),
                volts: 2.,
            },
            Element::Resistor {
                id: "RCONTROL".into(),
                p: NodeId(1),
                n: NodeId(0),
                ohms: 1.,
            },
            Element::Ccvs {
                id: "H1".into(),
                p: NodeId(2),
                n: NodeId(0),
                control_id: "V1".into(),
                ohms: 3.,
            },
            Element::Resistor {
                id: "ROUT".into(),
                p: NodeId(2),
                n: NodeId(0),
                ohms: 1.,
            },
        ],
    };
    assert_eq!(
        scalar_kind(&circuit, "H1").unwrap(),
        ScalarKind::Transresistance
    );
    let varied = replace_scalar(&circuit, "H1", -1.).unwrap();
    let x = operating_point(&compile(&varied).unwrap(), &DenseLu).unwrap();
    assert!((x[1] - 2.).abs() < 1e-12);
    assert_eq!(
        scalar_kind(&circuit, "H1").unwrap(),
        ScalarKind::Transresistance
    );
}

#[test]
fn ccvs_power_accounting_balances_the_control_supply() {
    let circuit = Circuit {
        nodes: vec!["0".into(), "in".into(), "out".into()],
        elements: vec![
            Element::Voltage {
                id: "V1".into(),
                p: NodeId(1),
                n: NodeId(0),
                volts: 2.,
            },
            Element::Resistor {
                id: "RCONTROL".into(),
                p: NodeId(1),
                n: NodeId(0),
                ohms: 1.,
            },
            Element::Ccvs {
                id: "H1".into(),
                p: NodeId(2),
                n: NodeId(0),
                control_id: "V1".into(),
                ohms: 3.,
            },
            Element::Resistor {
                id: "ROUT".into(),
                p: NodeId(2),
                n: NodeId(0),
                ohms: 1.,
            },
        ],
    };
    let plan = compile(&circuit).unwrap();
    let x = operating_point(&plan, &DenseLu).unwrap();
    assert!(dc_power(&plan, &x).unwrap().sum_absorbed_w.abs() < 1e-12);
}

#[test]
fn ccvs_json_request_is_solved_through_the_public_api() {
    let request: Request = serde_json::from_str(
        r#"{
      "schema_version":"1","case_id":"ccvs-json","circuit":{"nodes":["0","in","out"],"elements":[
        {"kind":"voltage","id":"V1","p":1,"n":0,"volts":2.0},
        {"kind":"resistor","id":"RCONTROL","p":1,"n":0,"ohms":1.0},
        {"kind":"ccvs","id":"H1","p":2,"n":0,"control_id":"V1","ohms":3.0},
        {"kind":"resistor","id":"ROUT","p":2,"n":0,"ohms":1.0}
      ]},"analysis":{"kind":"operating_point"}}"#,
    )
    .unwrap();
    let result = run(&request).unwrap();
    assert!((result.samples[0].values[1] + 6.).abs() < 1e-12);
}

#[test]
fn controlled_source_control_indices_and_gains_are_independently_validated() {
    use circuit_simulator::error::ErrorCode;
    // Every other terminal and component is valid. Test validation directly so
    // an unrelated topology/compiler rejection cannot mask a missing guard.
    for voltage_source in [false, true] {
        let make = |cp, cn, gain| Circuit {
            nodes: vec!["0".into(), "control".into(), "out".into()],
            elements: vec![
                Element::Voltage {
                    id: "V1".into(),
                    p: NodeId(1),
                    n: NodeId(0),
                    volts: 2.,
                },
                if voltage_source {
                    Element::Vcvs {
                        id: "E1".into(),
                        p: NodeId(2),
                        n: NodeId(0),
                        cp: NodeId(cp),
                        cn: NodeId(cn),
                        gain,
                    }
                } else {
                    Element::Vccs {
                        id: "G1".into(),
                        p: NodeId(2),
                        n: NodeId(0),
                        cp: NodeId(cp),
                        cn: NodeId(cn),
                        siemens: gain,
                    }
                },
                Element::Resistor {
                    id: "R1".into(),
                    p: NodeId(2),
                    n: NodeId(0),
                    ohms: 1.,
                },
            ],
        };
        for (cp, cn) in [(1, 0), (0, 1)] {
            for gain in [-0.5, 0., 0.5] {
                let circuit = make(cp, cn, gain);
                circuit.validate().unwrap();
                compile(&circuit).unwrap();
            }
        }
        // len is the first invalid index; ground remains a valid control node.
        for (cp, cn) in [(3, 0), (1, 3), (3, 4), (usize::MAX, 0), (1, usize::MAX)] {
            let circuit = make(cp, cn, 0.5);
            assert_eq!(
                circuit.validate().unwrap_err().code,
                ErrorCode::InvalidInput
            );
            assert_eq!(compile(&circuit).unwrap_err().code, ErrorCode::InvalidInput);
        }
        for gain in [f64::NAN, f64::INFINITY, f64::NEG_INFINITY] {
            let circuit = make(1, 0, gain);
            assert_eq!(
                circuit.validate().unwrap_err().code,
                ErrorCode::InvalidInput
            );
            assert_eq!(compile(&circuit).unwrap_err().code, ErrorCode::InvalidInput);
        }
    }
}
