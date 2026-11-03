//! Backend-neutral canonical systems and independent numerical acceptance.
//! Analyses and prepared providers share these checks without depending on DC policy.

use crate::{
    error::{ErrorCode, SimError, SimResult},
    linalg::BackendCapabilities,
    sparse::{CscMatrix, CscPattern},
};
use num_complex::Complex64;

/// Real values in the exact deterministic slot order of a CSC pattern.
#[derive(Debug, Clone)]
pub struct SparseRealSystem {
    pattern: CscPattern,
    values: Vec<f64>,
}
impl SparseRealSystem {
    /// Creates a finite real sparse system without exposing a vendor matrix type.
    pub fn new(pattern: CscPattern, values: Vec<f64>) -> SimResult<Self> {
        if values.len() != pattern.nnz() || values.iter().any(|value| !value.is_finite()) {
            return Err(SimError::invalid(
                "sparse real values do not match the pattern",
            ));
        }
        Ok(Self { pattern, values })
    }
    /// Canonical structural identity supplied to backend analysis.
    pub fn pattern(&self) -> &CscPattern {
        &self.pattern
    }
    /// Finite values ordered by `pattern().slots()`.
    pub fn values(&self) -> &[f64] {
        &self.values
    }
    /// Validates a right-hand side without allocating adapter-specific buffers.
    pub fn validate_rhs(&self, rhs: &[f64]) -> SimResult<()> {
        if rhs.len() != self.pattern.dimension() || rhs.iter().any(|value| !value.is_finite()) {
            return Err(SimError::invalid("invalid sparse real right-hand side"));
        }
        Ok(())
    }
}

/// Complex values in the exact deterministic slot order of a CSC pattern.
#[derive(Debug, Clone)]
pub struct SparseComplexSystem {
    pattern: CscPattern,
    values: Vec<Complex64>,
}
impl SparseComplexSystem {
    /// Assemble G+j*scale*Q directly over canonical compiler patterns. Signed
    /// flux rows are preserved; no dense matrix or vendor layout is involved.
    pub fn from_real_parts(real: &CscMatrix, imaginary: &CscMatrix, scale: f64) -> SimResult<Self> {
        if !scale.is_finite() {
            return Err(SimError::invalid("nonfinite sparse imaginary scale"));
        }
        let pattern = real.pattern().union(imaginary.pattern())?;
        let values = pattern
            .slots()
            .map(|(row, column)| {
                Complex64::new(
                    real.coefficient(row, column),
                    scale * imaginary.coefficient(row, column),
                )
            })
            .collect();
        Self::new(pattern, values)
    }
    /// Creates a finite complex sparse system with the same structural contract as real solves.
    pub fn new(pattern: CscPattern, values: Vec<Complex64>) -> SimResult<Self> {
        if values.len() != pattern.nnz()
            || values
                .iter()
                .any(|value| !value.re.is_finite() || !value.im.is_finite())
        {
            return Err(SimError::invalid(
                "sparse complex values do not match the pattern",
            ));
        }
        Ok(Self { pattern, values })
    }
    /// Canonical structural identity supplied to backend analysis.
    pub fn pattern(&self) -> &CscPattern {
        &self.pattern
    }
    /// Finite complex values ordered by `pattern().slots()`.
    pub fn values(&self) -> &[Complex64] {
        &self.values
    }
    /// Validates a real excitation vector for the current AC contract.
    pub fn validate_rhs(&self, rhs: &[f64]) -> SimResult<()> {
        if rhs.len() != self.pattern.dimension() || rhs.iter().any(|value| !value.is_finite()) {
            return Err(SimError::invalid("invalid sparse complex right-hand side"));
        }
        Ok(())
    }

    /// Validate a genuine complex excitation before any adapter indexing.
    pub fn validate_complex_rhs(&self, rhs: &[Complex64]) -> SimResult<()> {
        if rhs.len() != self.pattern.dimension()
            || rhs
                .iter()
                .any(|value| !value.re.is_finite() || !value.im.is_finite())
        {
            return Err(SimError::invalid("invalid sparse complex excitation"));
        }
        Ok(())
    }
}

/// Narrow adapter contract shared by sparse CPU, SIMD and accelerator experiments.
///
/// Implementations own all layout conversion, factor lifetime, device placement
/// and vendor/native handles. Unsupported operations must return `Unsupported`
/// rather than silently routing through a different provider.
pub trait SparseLinearBackend {
    /// Declares placement, precision and operation support without exposing handles.
    fn sparse_capabilities(&self) -> BackendCapabilities;
    /// Solves one validated real sparse system.
    fn solve_sparse_real(&self, system: &SparseRealSystem, rhs: &[f64]) -> SimResult<Vec<f64>>;
    /// Solves one validated complex sparse system with a real excitation vector.
    fn solve_sparse_complex(
        &self,
        system: &SparseComplexSystem,
        rhs: &[f64],
    ) -> SimResult<Vec<Complex64>>;
    /// Genuine complex RHS, retaining a validated compatibility implementation
    /// for adapters that only implement the historical real-excitation method.
    fn solve_sparse_complex_rhs(
        &self,
        system: &SparseComplexSystem,
        rhs: &[Complex64],
    ) -> SimResult<Vec<Complex64>> {
        system.validate_complex_rhs(rhs)?;
        crate::linalg::complex_rhs_via_real(system.pattern().dimension(), rhs, |part| {
            self.solve_sparse_complex(system, part)
        })
    }
}

/// Componentwise CSC backward error, independent of factorization or analysis.
/// Reject nonfinite accumulation and errors above the shared 1e-10 policy.
pub fn verify_csc_solution(
    matrix: &crate::sparse::CscMatrix,
    rhs: &[f64],
    values: &[f64],
) -> SimResult<f64> {
    if rhs.len() != matrix.dimension() || rhs.iter().any(|value| !value.is_finite()) {
        return Err(SimError::invalid("invalid sparse real excitation"));
    }
    if values.len() != matrix.dimension() || values.iter().any(|value| !value.is_finite()) {
        return Err(SimError::new(
            ErrorCode::NumericalFailure,
            "invalid sparse real solution",
        ));
    }
    let mut sums = vec![0.; matrix.dimension()];
    let mut scales: Vec<_> = rhs.iter().map(|value| value.abs()).collect();
    for (row, column, coefficient) in matrix.entries() {
        let product = coefficient * values[column];
        sums[row] += product;
        scales[row] += product.abs();
    }
    let mut worst = 0_f64;
    for row in 0..matrix.dimension() {
        let error = (sums[row] - rhs[row]).abs() / scales[row].max(f64::MIN_POSITIVE);
        if !scales[row].is_finite() || !error.is_finite() || error > 1e-10 {
            return Err(SimError::new(
                ErrorCode::NumericalFailure,
                "sparse real residual failed",
            ));
        }
        worst = worst.max(error);
    }
    Ok(worst)
}

/// Componentwise complex CSC backward error, before applying an acceptance
/// threshold. Finite scores greater than the policy limit remain visible for
/// bounded factor-internal refinement; this is not an acceptance verdict.
pub fn complex_backward_error(
    system: &SparseComplexSystem,
    rhs: &[Complex64],
    values: &[Complex64],
) -> SimResult<f64> {
    system.validate_complex_rhs(rhs)?;
    let n = system.pattern().dimension();
    crate::linalg::validate_complex_solution(n, values)?;
    let mut sums = vec![Complex64::new(0., 0.); n];
    let mut scales: Vec<_> = rhs.iter().map(|v| v.norm()).collect();
    for ((row, column), coefficient) in system.pattern().slots().zip(system.values()) {
        let term = *coefficient * values[column];
        sums[row] += term;
        scales[row] += term.norm();
    }
    let mut worst = 0_f64;
    for row in 0..n {
        let relative = (sums[row] - rhs[row]).norm() / scales[row].max(f64::MIN_POSITIVE);
        if !scales[row].is_finite() || !relative.is_finite() {
            return Err(SimError::new(
                ErrorCode::NumericalFailure,
                "AC residual failed",
            ));
        }
        worst = worst.max(relative);
    }
    Ok(worst)
}

/// Independently accept complex CSC solutions under the unchanged 1e-10
/// componentwise policy. The AC public entry point forwards here for compatibility.
pub fn verify_complex_solution(
    system: &SparseComplexSystem,
    rhs: &[Complex64],
    values: &[Complex64],
) -> SimResult<f64> {
    let error = complex_backward_error(system, rhs, values)?;
    if error > 1e-10 {
        return Err(SimError::new(
            ErrorCode::NumericalFailure,
            "AC residual failed",
        ));
    }
    Ok(error)
}
