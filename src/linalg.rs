use crate::{
    error::{ErrorCode, SimError, SimResult},
    sparse::CscMatrix,
};
/// Row-major binary64 reference matrix, limited before dense allocation.
#[derive(Debug, Clone)]
pub struct Matrix {
    pub n: usize,
    pub values: Vec<f64>,
}
impl Matrix {
    pub fn zeros(n: usize) -> SimResult<Self> {
        if n == 0 || n > 256 {
            return Err(SimError::invalid("matrix dimension must be 1..256"));
        }
        Ok(Self {
            n,
            values: vec![0.0; n * n],
        })
    }
    pub(crate) fn add(&mut self, row: usize, col: usize, value: f64) {
        self.values[row * self.n + col] += value;
    }
    pub(crate) fn at(&self, row: usize, col: usize) -> f64 {
        self.values[row * self.n + col]
    }
    pub fn validate(&self, rhs: &[f64]) -> SimResult<()> {
        if self.n == 0
            || self.n > 256
            || self.values.len() != self.n * self.n
            || rhs.len() != self.n
        {
            return Err(SimError::invalid("invalid linear system dimensions"));
        }
        if self.values.iter().chain(rhs).any(|v| !v.is_finite()) {
            return Err(SimError::invalid("nonfinite matrix/RHS"));
        }
        Ok(())
    }
}
/// Adapters know matrices, never netlists or device classes.
pub trait LinearSolver: Send + Sync {
    /// A prepared session owns numeric factors; None means preparation is unsupported.
    fn prepare(&self, _matrix: &Matrix) -> SimResult<Option<PreparedReal>> {
        Ok(None)
    }
    fn name(&self) -> &'static str;
    fn solve(&self, matrix: &Matrix, rhs: &[f64]) -> SimResult<Vec<f64>>;
    /// Solves an owned compiler CSC plan without a dense materialization.
    ///
    /// Backends that have no direct sparse path return `Unsupported`; analysis
    /// drivers may then use their documented dense reference fallback. Any
    /// other error is a real backend failure and must not be hidden by a
    /// fallback to a different numerical method.
    fn solve_csc(&self, _matrix: &CscMatrix, _rhs: &[f64]) -> SimResult<Vec<f64>> {
        Err(SimError::new(
            ErrorCode::Unsupported,
            "backend has no direct canonical CSC implementation",
        ))
    }
    /// Direct canonical complex CSC solve. Only Unsupported permits an analysis
    /// to use this same provider's documented dense-reference fallback.
    fn solve_complex_csc(
        &self,
        _system: &crate::solver_contract::SparseComplexSystem,
        _rhs: &[num_complex::Complex64],
    ) -> SimResult<Vec<num_complex::Complex64>> {
        Err(SimError::new(
            ErrorCode::Unsupported,
            "backend has no direct complex CSC implementation",
        ))
    }
    /// Genuine complex excitation. The compatibility default superposes two
    /// real-excitation solves; native complex providers override it to reuse one factor.
    fn solve_complex_rhs(
        &self,
        real: &Matrix,
        imag: &Matrix,
        rhs: &[num_complex::Complex64],
    ) -> SimResult<Vec<num_complex::Complex64>> {
        real.validate(&vec![0.; real.n.min(256)])?;
        imag.validate(&vec![0.; imag.n.min(256)])?;
        if real.n != imag.n {
            return Err(SimError::invalid("complex matrix dimensions differ"));
        }
        complex_rhs_via_real(real.n, rhs, |part| self.solve_complex(real, imag, part))
    }
    fn solve_complex(
        &self,
        _real: &Matrix,
        _imag: &Matrix,
        _rhs: &[f64],
    ) -> SimResult<Vec<num_complex::Complex64>> {
        Err(crate::error::SimError::new(
            crate::error::ErrorCode::Unsupported,
            "backend has no complex implementation",
        ))
    }
    fn capabilities(&self) -> BackendCapabilities {
        BackendCapabilities {
            id: self.name(),
            placement: "cpu",
            max_unknowns: 256,
            sparse: false,
            gpu: false,
            complex: false,
            prepared_real: false,
            provider_version: "caller_defined",
        }
    }
}

/// Compatibility glue only; shape/finite checks apply to each response before
/// zip can hide a short quadrature vector. Provider errors propagate unchanged.
pub(crate) fn complex_rhs_via_real(
    n: usize,
    rhs: &[num_complex::Complex64],
    solve: impl Fn(&[f64]) -> SimResult<Vec<num_complex::Complex64>>,
) -> SimResult<Vec<num_complex::Complex64>> {
    if rhs.len() != n || rhs.iter().any(|v| !v.re.is_finite() || !v.im.is_finite()) {
        return Err(SimError::invalid("invalid complex right-hand side"));
    }
    let real: Vec<_> = rhs.iter().map(|v| v.re).collect();
    let imag: Vec<_> = rhs.iter().map(|v| v.im).collect();
    let mut values = solve(&real)?;
    validate_complex_solution(n, &values)?;
    if imag.iter().any(|v| *v != 0.) {
        let quadrature = solve(&imag)?;
        validate_complex_solution(n, &quadrature)?;
        for (value, q) in values.iter_mut().zip(quadrature) {
            *value += num_complex::Complex64::new(-q.im, q.re);
        }
        validate_complex_solution(n, &values)?;
    }
    Ok(values)
}

pub(crate) fn validate_complex_solution(
    n: usize,
    values: &[num_complex::Complex64],
) -> SimResult<()> {
    if values.len() != n
        || values
            .iter()
            .any(|v| !v.re.is_finite() || !v.im.is_finite())
    {
        return Err(SimError::new(
            ErrorCode::NumericalFailure,
            "backend returned invalid complex solution",
        ));
    }
    Ok(())
}

/// Immutable matrix identity prevents stale-factor reuse after caller mutations.
/// Session-local closure intentionally has no Send/Sync promise.
pub struct PreparedReal {
    matrix: Matrix,
    solve: Box<PreparedSolve>,
}
type PreparedSolve = dyn Fn(&[f64]) -> SimResult<Vec<f64>>;
impl PreparedReal {
    pub fn new(
        matrix: Matrix,
        solve: impl Fn(&[f64]) -> SimResult<Vec<f64>> + 'static,
    ) -> SimResult<Self> {
        matrix.validate(&vec![0.0; matrix.n.min(256)])?;
        Ok(Self {
            matrix,
            solve: Box::new(solve),
        })
    }
    pub fn matrix(&self) -> &Matrix {
        &self.matrix
    }
    pub fn solve(&self, rhs: &[f64]) -> SimResult<Vec<f64>> {
        self.matrix.validate(rhs)?;
        let x = (self.solve)(rhs)?;
        if x.len() != self.matrix.n || x.iter().any(|v| !v.is_finite()) {
            return Err(SimError::new(
                crate::error::ErrorCode::NumericalFailure,
                "invalid prepared solution",
            ));
        }
        Ok(x)
    }
}

/// Capability discovery is explicit; GPU presence never silently changes execution.
#[derive(Debug, serde::Serialize)]
pub struct BackendCapabilities {
    pub id: &'static str,
    pub placement: &'static str,
    pub max_unknowns: usize,
    pub sparse: bool,
    pub gpu: bool,
    pub complex: bool,
    pub prepared_real: bool,
    pub provider_version: &'static str,
}
