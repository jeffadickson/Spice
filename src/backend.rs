use crate::{error::{ErrorCode,SimError,SimResult},linalg::{LinearSolver,Matrix}};
use nalgebra::{DMatrix,DVector};
/// Dense diagnostic adapter; it is not a production sparse backend.
#[derive(Debug,Default)]
pub struct DenseLu;
impl LinearSolver for DenseLu{
    fn name(&self)->&'static str{"nalgebra_dense_lu_reference"}
    fn solve(&self,a:&Matrix,b:&[f64])->SimResult<Vec<f64>>{
        a.validate(b)?;
        let matrix=DMatrix::from_row_slice(a.n,a.n,&a.values);
        let rhs=DVector::from_column_slice(b);
        let x=matrix.lu().solve(&rhs).ok_or_else(||SimError::new(ErrorCode::SingularMatrix,"LU has no unique solution"))?;
        if x.iter().any(|v|!v.is_finite()){return Err(SimError::new(ErrorCode::NumericalFailure,"nonfinite solution"));}
        Ok(x.as_slice().to_vec())
    }
}

/// Independent algorithm within the same library, not a second qualified provider.
#[derive(Debug,Default)]
pub struct DenseQr;
impl LinearSolver for DenseQr{
    fn name(&self)->&'static str{"nalgebra_dense_qr_reference"}
    fn solve(&self,a:&Matrix,b:&[f64])->SimResult<Vec<f64>>{
        a.validate(b)?;
        let x=DMatrix::from_row_slice(a.n,a.n,&a.values).qr()
            .solve(&DVector::from_column_slice(b))
            .ok_or_else(||SimError::new(ErrorCode::SingularMatrix,"QR has no unique solution"))?;
        if x.iter().any(|v|!v.is_finite()){return Err(SimError::new(ErrorCode::NumericalFailure,"nonfinite QR solution"));}
        Ok(x.as_slice().to_vec())
    }
}

