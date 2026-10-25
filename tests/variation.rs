use circuit_simulator::{
    api::Request,
    circuit::{Circuit, Element},
    ids::NodeId,
    registry::BackendRegistry,
    sweep::{SweepSummary, derived_case_id, run_grid, run_scalar, run_scalar_cancellable},
    variation::{ScalarGrid, ScalarKind, ScalarSweep, replace_scalar, scalar_kind},
};

fn fixture() -> Circuit {
    Circuit {
        nodes: vec!["0".into(), "out".into()],
        elements: vec![Element::Resistor {
            id: "R1".into(),
            p: NodeId(1),
            n: NodeId(0),
            ohms: 10.,
        }],
    }
}

#[test]
fn derived_case_ids_are_bounded_and_kind_specific() {
    assert_eq!(derived_case_id("base", 's', 2).unwrap(), "base--s02");
    assert_ne!(derived_case_id("base", 's', 2).unwrap(), derived_case_id("base", 'g', 2).unwrap());
    assert!(derived_case_id("", 's', 0).is_err());
    assert!(derived_case_id("base", 'x', 0).is_err());
}

#[test]
fn scalar_kind_is_explicit_for_programmatic_consumers() {
    assert_eq!(scalar_kind(&fixture(), "R1").unwrap(), ScalarKind::Resistance);
    assert_eq!(ScalarKind::Resistance.unit(), "ohm");
    assert!(scalar_kind(&fixture(), "nope").is_err());
}

#[test]
fn selection_carries_identity_value_and_si_unit() {
    let sweep = ScalarSweep { element_id: "R1".into(), values: vec![10.] };
    let selection = sweep.selection(&fixture(), 10.).unwrap();
    assert_eq!(selection.element_id, "R1");
    assert_eq!(selection.unit, "ohm");
    assert!(sweep.selection(&fixture(), 11.).is_err());
}

#[test]
fn sweep_summary_makes_per_point_accounting_explicit() {
    let request: Request = serde_json::from_str(include_str!("../examples/divider.json")).unwrap();
    let sweep = ScalarSweep {
        element_id: "R1".into(),
        values: vec![1000., 2000.],
    };
    let points = run_scalar(&request, &sweep, &BackendRegistry::reference()).unwrap();
    assert_eq!(
        SweepSummary::from_points(&points),
        SweepSummary {
            total: 2,
            succeeded: 2,
            failed: 0
        }
    );
}

#[test]
fn sweep_retains_failed_points_without_discarding_their_identity() {
    let mut request: Request =
        serde_json::from_str(include_str!("../examples/divider.json")).unwrap();
    request.backend = "unregistered".into();
    let sweep = ScalarSweep {
        element_id: "R1".into(),
        values: vec![1000.],
    };
    let points = run_scalar(&request, &sweep, &BackendRegistry::reference()).unwrap();
    assert_eq!(points[0].execution, "failed");
    assert_eq!(points[0].index, 0);
    assert_eq!(points[0].selection.value, 1000.);
    assert!(points[0].error.is_some());
}

#[test]
fn grid_execution_exposes_each_selected_vector() {
    let request: Request = serde_json::from_str(include_str!("../examples/divider.json")).unwrap();
    let grid = ScalarGrid {
        dimensions: vec![ScalarSweep {
            element_id: "R1".into(),
            values: vec![1000., 2000.],
        }],
    };
    let points = run_grid(&request, &grid, &BackendRegistry::reference()).unwrap();
    assert_eq!(points.len(), 2);
    assert_eq!(points[1].selections[0].value, 2000.);
    assert_eq!(points[1].result.as_ref().unwrap().case_id, "divider--g01");
}

#[test]
fn grid_has_deterministic_cartesian_order() {
    let mut circuit = fixture();
    circuit.elements.push(Element::Voltage {
        id: "V1".into(),
        p: NodeId(1),
        n: NodeId(0),
        volts: 1.,
    });
    let grid = ScalarGrid {
        dimensions: vec![
            ScalarSweep {
                element_id: "R1".into(),
                values: vec![10., 20.],
            },
            ScalarSweep {
                element_id: "V1".into(),
                values: vec![1., 2.],
            },
        ],
    };
    let rows = grid.circuits(&circuit).unwrap();
    assert_eq!(
        rows.iter().map(|row| row.0.clone()).collect::<Vec<_>>(),
        vec![vec![10., 1.], vec![10., 2.], vec![20., 1.], vec![20., 2.]]
    );
}

#[test]
fn cancellation_is_observed_between_sweep_points() {
    let request: Request = serde_json::from_str(include_str!("../examples/divider.json")).unwrap();
    let sweep = ScalarSweep {
        element_id: "R1".into(),
        values: vec![1000.],
    };
    let cancelled = std::sync::atomic::AtomicBool::new(true);
    let error = run_scalar_cancellable(&request, &sweep, &BackendRegistry::reference(), &cancelled)
        .unwrap_err();
    assert!(error.message.contains("cancelled"));
}

#[test]
fn scalar_sweep_returns_stable_indexed_cases() {
    let request: Request = serde_json::from_str(include_str!("../examples/divider.json")).unwrap();
    let sweep = ScalarSweep {
        element_id: "R1".into(),
        values: vec![1000., 2000.],
    };
    let points = run_scalar(&request, &sweep, &BackendRegistry::reference()).unwrap();
    assert_eq!(points.len(), 2);
    assert_eq!(points[0].result.as_ref().unwrap().case_id, "divider--s00");
    assert_eq!(points[1].result.as_ref().unwrap().case_id, "divider--s01");
    assert!(points.iter().all(|point| point.execution == "succeeded"));
}

#[test]
fn sweep_preserves_declared_order_and_rejects_unbounded_input() {
    let sweep = ScalarSweep {
        element_id: "R1".into(),
        values: vec![20., 10.],
    };
    let values: Vec<_> = sweep
        .circuits(&fixture())
        .unwrap()
        .into_iter()
        .map(|c| match c.elements[0] {
            Element::Resistor { ohms, .. } => ohms,
            _ => unreachable!(),
        })
        .collect();
    assert_eq!(values, vec![20., 10.]);
    assert!(
        ScalarSweep {
            element_id: "R1".into(),
            values: vec![]
        }
        .validate()
        .is_err()
    );
    assert!(
        ScalarSweep {
            element_id: "R1".into(),
            values: vec![f64::NAN]
        }
        .validate()
        .is_err()
    );
}

#[test]
fn replacement_is_immutable_and_validated() {
    let original = fixture();
    let varied = replace_scalar(&original, "R1", 20.).unwrap();
    assert!(matches!(original.elements[0], Element::Resistor { ohms, .. } if ohms == 10.));
    assert!(matches!(varied.elements[0], Element::Resistor { ohms, .. } if ohms == 20.));
    assert!(replace_scalar(&original, "R1", 0.).is_err());
    assert!(replace_scalar(&original, "missing", 1.).is_err());
}
