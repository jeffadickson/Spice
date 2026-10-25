use crate::{
    error::{SimError, SimResult},
    linalg::Matrix,
};
/// Canonical CSC: sorted unique rows per column; no unchecked external constructors.
#[derive(Debug, Clone)]
pub struct CscMatrix {
    n: usize,
    col_ptr: Vec<usize>,
    rows: Vec<usize>,
    values: Vec<f64>,
}
impl CscMatrix {
    pub fn new(
        n: usize,
        col_ptr: Vec<usize>,
        rows: Vec<usize>,
        values: Vec<f64>,
    ) -> SimResult<Self> {
        if !(1..=256).contains(&n)
            || col_ptr.len() != n + 1
            || rows.len() != values.len()
            || rows.len() > n * n
            || col_ptr.first() != Some(&0)
            || col_ptr.last() != Some(&rows.len())
            || col_ptr.windows(2).any(|w| w[0] > w[1])
            || col_ptr.iter().any(|v| *v > rows.len())
            || rows.iter().any(|r| *r >= n)
            || values.iter().any(|v| !v.is_finite())
        {
            return Err(SimError::invalid("invalid CSC shape, index or values"));
        }
        for col in 0..n {
            if rows[col_ptr[col]..col_ptr[col + 1]]
                .windows(2)
                .any(|w| w[0] >= w[1])
            {
                return Err(SimError::invalid("CSC rows must be strictly sorted"));
            }
        }
        Ok(Self {
            n,
            col_ptr,
            rows,
            values,
        })
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
        self.n
    }
    pub fn nnz(&self) -> usize {
        self.values.len()
    }
    pub fn entries(&self) -> impl Iterator<Item = (usize, usize, f64)> + '_ {
        (0..self.n).flat_map(move |j| {
            (self.col_ptr[j]..self.col_ptr[j + 1]).map(move |k| (self.rows[k], j, self.values[k]))
        })
    }
}
