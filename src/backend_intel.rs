//! Experimental Intel GPU linear provider; no CPU numerical fallback.
use crate::{
    error::{ErrorCode, SimError, SimResult},
    linalg::{BackendCapabilities, LinearSolver, Matrix},
};
use num_complex::Complex64;

/// Stateless adapter; the separately loaded native provider owns and serializes
/// its physical OpenCL context. Assembly/Newton/time control stay on the CPU.
pub struct IntelOpenCl;

fn gpu_error(message: String) -> SimError {
    SimError::new(ErrorCode::NumericalFailure, message)
}

/// Validate the original unscaled system independently of the native library.
fn verified(a: &Matrix, b: &[f64], x: Vec<f64>) -> SimResult<Vec<f64>> {
    if x.len() != a.n || x.iter().any(|v| !v.is_finite()) {
        return Err(gpu_error("Invalid GPU solution shape/values".into()));
    }
    for i in 0..a.n {
        let mut residual = -b[i];
        let mut scale = b[i].abs();
        for j in 0..a.n {
            let term = a.values[i * a.n + j] * x[j];
            residual += term;
            scale += term.abs();
        }
        let error = if scale == 0. {
            residual.abs()
        } else {
            residual.abs() / scale
        };
        if !residual.is_finite() || !scale.is_finite() || !error.is_finite() || error > 1e-10 {
            return Err(gpu_error(format!(
                "GPU componentwise backward error at row {i}: {error}"
            )));
        }
    }
    Ok(x)
}
impl LinearSolver for IntelOpenCl {
    fn name(&self) -> &'static str {
        "intel_opencl_viennacl_gmres_experimental"
    }
    fn capabilities(&self) -> BackendCapabilities {
        BackendCapabilities {
            id: self.name(),
            placement: "gpu",
            max_unknowns: 256,
            sparse: false,
            gpu: true,
            complex: true,
            prepared_real: false,
            provider_version: "ViennaCL 1.7.1 OpenCL FP64; ABI 1",
        }
    }
    fn solve(&self, a: &Matrix, b: &[f64]) -> SimResult<Vec<f64>> {
        a.validate(b)?;
        verified(
            a,
            b,
            spice_intel_host::solve(a.n, &a.values, b).map_err(gpu_error)?,
        )
    }
    fn solve_complex(&self, real: &Matrix, imag: &Matrix, b: &[f64]) -> SimResult<Vec<Complex64>> {
        self.solve_complex_rhs(
            real,
            imag,
            &b.iter().map(|v| Complex64::new(*v, 0.)).collect::<Vec<_>>(),
        )
    }
    fn solve_complex_rhs(
        &self,
        real: &Matrix,
        imag: &Matrix,
        b: &[Complex64],
    ) -> SimResult<Vec<Complex64>> {
        real.validate(&vec![0.; real.n.min(256)])?;
        imag.validate(&vec![0.; imag.n.min(256)])?;
        if real.n != imag.n
            || b.len() != real.n
            || b.iter().any(|v| !v.re.is_finite() || !v.im.is_finite())
        {
            return Err(SimError::invalid("Invalid GPU complex system"));
        }
        // [Re A -Im A; Im A Re A] [Re x; Im x] = [Re b; Im b].
        // This is an exact algebraic embedding, not two unrelated real solves.
        let n = real.n;
        let m = 2 * n;
        let mut a = vec![0.; m * m];
        let mut rhs = vec![0.; m];
        for i in 0..n {
            rhs[i] = b[i].re;
            rhs[i + n] = b[i].im;
            for j in 0..n {
                a[i * m + j] = real.values[i * n + j];
                a[i * m + j + n] = -imag.values[i * n + j];
                a[(i + n) * m + j] = imag.values[i * n + j];
                a[(i + n) * m + j + n] = real.values[i * n + j];
            }
        }
        let x = spice_intel_host::solve(m, &a, &rhs).map_err(gpu_error)?;
        // The native gate checks the embedded rows; also check real and imaginary
        // rows in safe owned code, without calling another numerical provider.
        let x = verified(&Matrix { n: m, values: a }, &rhs, x)?;
        Ok((0..n).map(|i| Complex64::new(x[i], x[i + n])).collect())
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn corrupt_and_nonfinite_gpu_results_are_rejected() {
        let a = Matrix {
            n: 2,
            values: vec![1., 1., 0., 1.],
        };
        assert!(verified(&a, &[2., 1.], vec![1., 1.]).is_ok());
        for x in [vec![0., 1.], vec![f64::NAN, 1.], vec![1.]] {
            assert!(verified(&a, &[2., 1.], x).is_err())
        }
    }
    #[test]
    fn overflowed_residual_scale_cannot_hide_an_incorrect_solution() {
        let a=Matrix{n:2,values:vec![1e308,1e308,1.,0.]};
        assert!(verified(&a,&[0.,1.],vec![1.,-0.9]).is_err());
    }
}
