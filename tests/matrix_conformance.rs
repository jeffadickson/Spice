//! Shared backend-neutral matrix corpus for adapter qualification spikes.
//!
//! The corpus deliberately stays below the reference engine's size budget, but
//! uses general unsymmetric systems and verifies physical residuals rather than
//! trusting a provider success status. Future CPU SIMD and accelerator adapters
//! must run these exact cases before circuit-level qualification.

use circuit_simulator::{
    backend::DenseLu,
    backend_faer::FaerSparseLu,
    solver_contract::{SparseComplexSystem, SparseLinearBackend, SparseRealSystem},
    sparse::CscPattern,
};
use num_complex::Complex64;
use std::collections::BTreeSet;

fn backends() -> [&'static dyn SparseLinearBackend; 2] {
    static DENSE: DenseLu = DenseLu;
    static FAER: FaerSparseLu = FaerSparseLu;
    [&DENSE, &FAER]
}

fn real_case() -> (SparseRealSystem, Vec<f64>, Vec<f64>) {
    // [[0, 2, 1], [3, -1, 0], [1, 0, 1]] * [1, 1, 1] = [3, 2, 2].
    let pattern = CscPattern::new(3, vec![0, 2, 4, 6], vec![1, 2, 0, 1, 0, 2]).unwrap();
    (
        SparseRealSystem::new(pattern, vec![3.0, 1.0, 2.0, -1.0, 1.0, 1.0]).unwrap(),
        vec![3.0, 2.0, 2.0],
        vec![1.0, 1.0, 1.0],
    )
}

fn complex_case() -> (SparseComplexSystem, Vec<f64>, Vec<Complex64>) {
    // [[1+j, 1], [0, 2-j]] * [.4-.8j, .8+.4j] = [2, 2].
    let pattern = CscPattern::new(2, vec![0, 1, 3], vec![0, 0, 1]).unwrap();
    (
        SparseComplexSystem::new(
            pattern,
            vec![
                Complex64::new(1.0, 1.0),
                Complex64::new(1.0, 0.0),
                Complex64::new(2.0, -1.0),
            ],
        )
        .unwrap(),
        vec![2.0, 2.0],
        vec![Complex64::new(0.4, -0.8), Complex64::new(0.8, 0.4)],
    )
}

fn real_residual(system: &SparseRealSystem, solution: &[f64], rhs: &[f64]) -> f64 {
    let mut residual = rhs.iter().map(|value| -*value).collect::<Vec<_>>();
    for ((row, column), value) in system.pattern().slots().zip(system.values()) {
        residual[row] += value * solution[column];
    }
    residual.into_iter().map(f64::abs).fold(0.0, f64::max)
}

fn complex_residual(system: &SparseComplexSystem, solution: &[Complex64], rhs: &[f64]) -> f64 {
    let mut residual = rhs
        .iter()
        .map(|value| Complex64::new(-*value, 0.0))
        .collect::<Vec<_>>();
    for ((row, column), value) in system.pattern().slots().zip(system.values()) {
        residual[row] += value * solution[column];
    }
    residual
        .into_iter()
        .map(Complex64::norm)
        .fold(0.0, f64::max)
}

#[test]
fn shared_real_unsymmetric_corpus_has_small_residuals_for_each_backend() {
    let (system, rhs, expected) = real_case();
    for backend in backends() {
        let solution = backend.solve_sparse_real(&system, &rhs).unwrap();
        assert!(
            real_residual(&system, &solution, &rhs) < 1e-12,
            "{}",
            backend.sparse_capabilities().id
        );
        for (actual, expected) in solution.iter().zip(&expected) {
            assert!(
                (actual - expected).abs() < 1e-12,
                "{}",
                backend.sparse_capabilities().id
            );
        }
    }
}

#[test]
fn shared_complex_unsymmetric_corpus_has_small_residuals_for_each_backend() {
    let (system, rhs, expected) = complex_case();
    for backend in backends() {
        let solution = backend.solve_sparse_complex(&system, &rhs).unwrap();
        assert!(
            complex_residual(&system, &solution, &rhs) < 1e-12,
            "{}",
            backend.sparse_capabilities().id
        );
        for (actual, expected) in solution.iter().zip(&expected) {
            assert!(
                (*actual - *expected).norm() < 1e-12,
                "{}",
                backend.sparse_capabilities().id
            );
        }
    }
}

#[test]
fn sparse_contract_rejects_nonfinite_values_and_wrong_rhs_lengths() {
    let pattern = CscPattern::new(1, vec![0, 1], vec![0]).unwrap();
    assert!(SparseRealSystem::new(pattern.clone(), vec![f64::NAN]).is_err());
    assert!(
        SparseComplexSystem::new(pattern.clone(), vec![Complex64::new(1.0, f64::NAN)]).is_err()
    );
    let real = SparseRealSystem::new(pattern, vec![1.0]).unwrap();
    assert!(real.validate_rhs(&[]).is_err());
    assert!(real.validate_rhs(&[f64::INFINITY]).is_err());
}

/// A deterministic, diagonally dominant corpus varies CSC dimensions and
/// nonsymmetric off-diagonal structure without turning qualification into a
/// flaky random test. The seed is intentionally retained in source so a
/// counterexample can be reproduced exactly on every supported host.
#[test]
fn seeded_real_sparse_corpus_has_known_solutions_for_each_backend() {
    let mut state = 0x5A45_5EED_u64;
    for case_index in 0..64 {
        state = state.wrapping_mul(6364136223846793005).wrapping_add(1);
        let n = 2 + (state as usize % 7);
        let mut columns = vec![BTreeSet::new(); n];
        for (column, entries) in columns.iter_mut().enumerate() {
            entries.insert(column);
            // Keep a small, changing sparse footprint in addition to every
            // diagonal slot. BTreeSet establishes canonical row ordering.
            for _ in 0..2 {
                state = state.wrapping_mul(6364136223846793005).wrapping_add(1);
                entries.insert((state as usize) % n);
            }
        }
        let mut pointers = vec![0];
        let mut rows = Vec::new();
        let mut values = Vec::new();
        for entries in &columns {
            for &row in entries {
                state = state.wrapping_mul(6364136223846793005).wrapping_add(1);
                let value = ((state >> 32) as f64 / u32::MAX as f64) - 0.5;
                rows.push(row);
                values.push(value);
            }
            pointers.push(rows.len());
        }
        // Replace each diagonal coefficient with strictly dominant magnitude.
        for column in 0..n {
            for slot in pointers[column]..pointers[column + 1] {
                if rows[slot] == column {
                    values[slot] = 10.;
                }
            }
        }
        let pattern = CscPattern::new(n, pointers, rows).unwrap();
        let system = SparseRealSystem::new(pattern, values).unwrap();
        let expected = (0..n)
            .map(|index| (index as f64 - 3.) / 7.)
            .collect::<Vec<_>>();
        let mut rhs = vec![0.; n];
        for ((row, column), value) in system.pattern().slots().zip(system.values()) {
            rhs[row] += value * expected[column];
        }
        for backend in backends() {
            let actual = backend.solve_sparse_real(&system, &rhs).unwrap();
            assert!(
                real_residual(&system, &actual, &rhs) < 1e-10,
                "case {case_index}, {}",
                backend.sparse_capabilities().id
            );
            for (actual, expected) in actual.iter().zip(&expected) {
                assert!(
                    (actual - expected).abs() < 1e-10,
                    "case {case_index}, {}",
                    backend.sparse_capabilities().id
                );
            }
        }
    }
}
