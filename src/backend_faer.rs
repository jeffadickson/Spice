use crate::{error::{ErrorCode,SimError,SimResult},linalg::{BackendCapabilities,LinearSolver,Matrix},sparse::CscMatrix};
use faer::{prelude::*,sparse::{SparseColMat,Triplet}};
/// Sparse factorization over the current bounded assembler, not a scalability claim.
#[derive(Debug,Default)]
pub struct FaerSparseLu;
impl LinearSolver for FaerSparseLu{
    fn name(&self)->&'static str{"faer_sparse_lu"}
    fn capabilities(&self)->BackendCapabilities{BackendCapabilities{id:self.name(),placement:"cpu",max_unknowns:256,sparse:true,gpu:false,complex:true,prepared_real:true,provider_version:"faer 0.24.4"}}
    fn solve(&self,a:&Matrix,b:&[f64])->SimResult<Vec<f64>>{
        a.validate(b)?;let csc=CscMatrix::from_dense(a)?;
        let entries:Vec<_>=csc.entries().map(|(i,j,v)|Triplet::new(i,j,v)).collect();
        let sparse=SparseColMat::<usize,f64>::try_new_from_triplets(a.n,a.n,&entries).map_err(|e|SimError::invalid(format!("sparse creation: {e:?}")))?;
        let lu=sparse.sp_lu().map_err(|e|SimError::new(ErrorCode::SingularMatrix,format!("sparse LU: {e:?}")))?;
        let rhs=faer::Mat::from_fn(a.n,1,|i,_|b[i]);let x=lu.solve(&rhs);
        let values:Vec<_>=(0..a.n).map(|i|x[(i,0)]).collect();
        if values.iter().any(|v|!v.is_finite()){return Err(SimError::new(ErrorCode::NumericalFailure,"nonfinite sparse solution"));}
        Ok(values)
    }
}

