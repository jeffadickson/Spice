//! Captured Newton correction system with an exact homogeneous singleton row.
use circuit_simulator::{
    backend::DenseLu,
    linalg::{LinearSolver, Matrix},
};

#[test]
fn grounded_source_constraint_is_exact_without_relaxing_any_original_equation() {
    let fixture: serde_json::Value =
        serde_json::from_str(include_str!("fixtures/bjt-zero-constraint-system.json")).unwrap();
    let n = fixture["n"].as_u64().unwrap() as usize;
    let values = fixture["values"]
        .as_array()
        .unwrap()
        .iter()
        .map(|v| v.as_f64().unwrap())
        .collect();
    let rhs: Vec<_> = fixture["rhs"]
        .as_array()
        .unwrap()
        .iter()
        .map(|v| v.as_f64().unwrap())
        .collect();
    let matrix = Matrix { n, values };
    let solution = DenseLu.solve(&matrix, &rhs).unwrap();
    assert_eq!(solution[1], 0.0);
    for row in 0..n {
        let products: Vec<_> = (0..n)
            .map(|column| matrix.values[row * n + column] * solution[column])
            .collect();
        let residual = (products.iter().sum::<f64>() - rhs[row]).abs();
        let scale = products.iter().map(|p| p.abs()).sum::<f64>() + rhs[row].abs();
        assert!(residual <= 1e-10 * scale.max(f64::MIN_POSITIVE));
    }
}
