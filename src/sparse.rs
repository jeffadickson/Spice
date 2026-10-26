use crate::{
    error::{SimError, SimResult},
    linalg::Matrix,
};
use std::collections::BTreeMap;

/// Compiler-owned deterministic sparse stamp accumulator.
/// Duplicate device stamps are summed before a canonical CSC plan is published.
#[derive(Debug)]
pub(crate) struct SparseAccumulator {
    n: usize,
    entries: BTreeMap<(usize, usize), f64>,
}
impl SparseAccumulator {
    pub(crate) fn new(n: usize) -> SimResult<Self> {
        // Validate the bounded profile without allocating a discarded dense matrix.
        if !(1..=256).contains(&n) {
            return Err(SimError::invalid("invalid sparse accumulator dimension"));
        }
        Ok(Self {
            n,
            entries: BTreeMap::new(),
        })
    }
    pub(crate) fn add(&mut self, row: usize, column: usize, value: f64) {
        *self.entries.entry((row, column)).or_insert(0.) += value;
    }
    pub(crate) fn to_csc(&self) -> SimResult<CscMatrix> {
        let mut columns = vec![Vec::<(usize, f64)>::new(); self.n];
        for (&(row, column), &value) in &self.entries {
            if value != 0. {
                columns[column].push((row, value));
            }
        }
        let mut pointers = vec![0];
        let mut rows = Vec::new();
        let mut values = Vec::new();
        for column in columns {
            for (row, value) in column {
                rows.push(row);
                values.push(value);
            }
            pointers.push(rows.len());
        }
        CscMatrix::new(self.n, pointers, rows, values)
    }
}
/// Backend-neutral canonical CSC structure with sorted unique rows per column.
///
/// The pattern deliberately contains no numerical values. It can therefore be
/// analyzed and retained by a sparse backend while a simulation changes only
/// its coefficients. No solver-library layout escapes this type.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct CscPattern {
    n: usize,
    col_ptr: Vec<usize>,
    rows: Vec<usize>,
}
impl CscPattern {
    /// Validates the canonical shape before any adapter receives it.
    pub fn new(n: usize, col_ptr: Vec<usize>, rows: Vec<usize>) -> SimResult<Self> {
        if !(1..=256).contains(&n)
            || col_ptr.len() != n + 1
            || rows.len() > n * n
            || col_ptr.first() != Some(&0)
            || col_ptr.last() != Some(&rows.len())
            || col_ptr.windows(2).any(|w| w[0] > w[1])
            || col_ptr.iter().any(|v| *v > rows.len())
            || rows.iter().any(|r| *r >= n)
        {
            return Err(SimError::invalid("invalid CSC shape or index"));
        }
        for col in 0..n {
            if rows[col_ptr[col]..col_ptr[col + 1]]
                .windows(2)
                .any(|w| w[0] >= w[1])
            {
                return Err(SimError::invalid("CSC rows must be strictly sorted"));
            }
        }
        Ok(Self { n, col_ptr, rows })
    }
    /// Matrix dimension shared by the pattern and all numerical value vectors.
    pub fn dimension(&self) -> usize {
        self.n
    }
    /// Number of structurally present coefficients.
    pub fn nnz(&self) -> usize {
        self.rows.len()
    }
    /// Canonical column pointers; adapters may convert them only inside their boundary.
    pub fn col_ptr(&self) -> &[usize] {
        &self.col_ptr
    }
    /// Canonical row indices; adapters may convert them only inside their boundary.
    pub fn rows(&self) -> &[usize] {
        &self.rows
    }
    /// Iterates the structural slots in deterministic column-major order.
    pub fn slots(&self) -> impl Iterator<Item = (usize, usize)> + '_ {
        (0..self.n).flat_map(move |j| {
            (self.col_ptr[j]..self.col_ptr[j + 1]).map(move |k| (self.rows[k], j))
        })
    }

    /// Canonical structural union without a dense scan. Numerical cancellation
    /// never removes a slot, so dynamic coefficient changes retain this identity.
    pub fn union(&self, other: &Self) -> SimResult<Self> {
        if self.n != other.n {
            return Err(SimError::invalid("CSC union dimensions differ"));
        }
        let mut pointers = vec![0];
        let mut rows = Vec::with_capacity((self.nnz() + other.nnz()).min(self.n * self.n));
        for column in 0..self.n {
            let mut left = self.col_ptr[column];
            let mut right = other.col_ptr[column];
            let left_end = self.col_ptr[column + 1];
            let right_end = other.col_ptr[column + 1];
            while left < left_end || right < right_end {
                let row = match (
                    self.rows.get(left).filter(|_| left < left_end),
                    other.rows.get(right).filter(|_| right < right_end),
                ) {
                    (Some(&a), Some(&b)) => a.min(b),
                    (Some(&a), None) => a,
                    (None, Some(&b)) => b,
                    (None, None) => unreachable!("nonempty merge remainder"),
                };
                rows.push(row);
                if left < left_end && self.rows[left] == row {
                    left += 1;
                }
                if right < right_end && other.rows[right] == row {
                    right += 1;
                }
            }
            pointers.push(rows.len());
        }
        Self::new(self.n, pointers, rows)
    }
}

/// Canonical real CSC values paired with a validated backend-neutral pattern.
#[derive(Debug, Clone)]
pub struct CscMatrix {
    pattern: CscPattern,
    values: Vec<f64>,
}
impl CscMatrix {
    pub fn new(
        n: usize,
        col_ptr: Vec<usize>,
        rows: Vec<usize>,
        values: Vec<f64>,
    ) -> SimResult<Self> {
        let pattern = CscPattern::new(n, col_ptr, rows)?;
        Self::from_pattern(pattern, values)
    }
    /// Pairs one finite real value with each canonical structural slot.
    pub fn from_pattern(pattern: CscPattern, values: Vec<f64>) -> SimResult<Self> {
        if values.len() != pattern.nnz() || values.iter().any(|v| !v.is_finite()) {
            return Err(SimError::invalid("invalid CSC values"));
        }
        Ok(Self { pattern, values })
    }
    pub fn from_dense(a: &Matrix) -> SimResult<Self> {
        a.validate(&vec![0.0; a.n.min(256)])?;
        let mut pointers = vec![0];
        let mut rows = Vec::new();
        let mut values = Vec::new();
        for j in 0..a.n {
            for i in 0..a.n {
                let value = a.values[i * a.n + j];
                if value != 0.0 {
                    rows.push(i);
                    values.push(value);
                }
            }
            pointers.push(rows.len());
        }
        Self::new(a.n, pointers, rows, values)
    }
    pub fn dimension(&self) -> usize {
        self.pattern.dimension()
    }
    pub fn nnz(&self) -> usize {
        self.values.len()
    }
    /// Structural identity used by sparse backend analysis.
    pub fn pattern(&self) -> &CscPattern {
        &self.pattern
    }
    /// Coefficients ordered by `pattern().slots()`.
    pub fn values(&self) -> &[f64] {
        &self.values
    }

    /// Linear combination over the canonical union, retaining zero-valued
    /// structural slots. Coefficients and every resulting value must be finite.
    pub fn combine_scaled(
        &self,
        other: &Self,
        left_scale: f64,
        right_scale: f64,
    ) -> SimResult<Self> {
        if !left_scale.is_finite() || !right_scale.is_finite() {
            return Err(SimError::invalid("nonfinite CSC combination scale"));
        }
        let pattern = self.pattern.union(&other.pattern)?;
        let values = pattern
            .slots()
            .map(|(row, column)| {
                left_scale * self.coefficient(row, column)
                    + right_scale * other.coefficient(row, column)
            })
            .collect();
        Self::from_pattern(pattern, values)
    }

    /// Lookup for an already validated union slot; missing entries are known
    /// mathematical zeros, not unknown device data.
    pub(crate) fn coefficient(&self, row: usize, column: usize) -> f64 {
        let begin = self.pattern.col_ptr[column];
        let end = self.pattern.col_ptr[column + 1];
        self.pattern.rows[begin..end]
            .binary_search(&row)
            .map(|index| self.values[begin + index])
            .unwrap_or(0.)
    }
    pub fn entries(&self) -> impl Iterator<Item = (usize, usize, f64)> + '_ {
        self.pattern
            .slots()
            .zip(self.values.iter().copied())
            .map(|((i, j), value)| (i, j, value))
    }
    /// Bounded reference materialization; production sparse adapters consume CSC directly.
    pub(crate) fn to_dense(&self) -> SimResult<Matrix> {
        let mut matrix = Matrix::zeros(self.dimension())?;
        for (row, column, value) in self.entries() {
            matrix.add(row, column, value);
        }
        Ok(matrix)
    }
}
