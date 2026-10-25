use crate::error::{SimError,SimResult};
/// Row-major binary64 reference matrix, limited before dense allocation.
#[derive(Debug,Clone)]
pub struct Matrix{pub n:usize,pub values:Vec<f64>}
impl Matrix{
    pub fn zeros(n:usize)->SimResult<Self>{
        if n==0||n>256{return Err(SimError::invalid("matrix dimension must be 1..256"));}
        Ok(Self{n,values:vec![0.0;n*n]})
    }
    pub fn add(&mut self,row:usize,col:usize,value:f64){self.values[row*self.n+col]+=value;}
    pub fn at(&self,row:usize,col:usize)->f64{self.values[row*self.n+col]}
    pub fn validate(&self,rhs:&[f64])->SimResult<()>{
        if self.n==0||self.n>256||self.values.len()!=self.n*self.n||rhs.len()!=self.n{return Err(SimError::invalid("invalid linear system dimensions"));}
        if self.values.iter().chain(rhs).any(|v|!v.is_finite()){return Err(SimError::invalid("nonfinite matrix/RHS"));}
        Ok(())
    }
}
/// Adapters know matrices, never netlists or device classes.
pub trait LinearSolver:Send+Sync{
    fn name(&self)->&'static str;
    fn solve(&self,matrix:&Matrix,rhs:&[f64])->SimResult<Vec<f64>>;
}

