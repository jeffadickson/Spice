use crate::{
    error::{SimError, SimResult},
    linalg::Matrix,
};
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
    pub fn entries(&self) -> impl Iterator<Item = (usize, usize, f64)> + '_ {
        self.pattern
            .slots()
            .zip(self.values.iter().copied())
            .map(|((i, j), value)| (i, j, value))
    }
}
