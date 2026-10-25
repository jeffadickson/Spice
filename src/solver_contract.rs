//! Backend-neutral sparse-system seam for future CPU SIMD and accelerator adapters.
//!
//! This is intentionally a small spike: the reference circuit assembler still
//! produces bounded dense matrices, while adapters can now accept a validated
//! canonical sparse pattern and a separate coefficient vector. Device models,
//! analyses and public request schemas do not depend on this module's adapters.

use crate::{
    error::{SimError, SimResult},
    linalg::BackendCapabilities,
    sparse::CscPattern,
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
}
