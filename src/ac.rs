use crate::{compile::CompiledCircuit,error::{ErrorCode,SimError,SimResult},quantity::positive};
use nalgebra::{DMatrix,DVector};
use num_complex::Complex64;
use serde::Serialize;
#[derive(Debug,Clone,Serialize)]
pub struct ComplexValue{pub real:f64,pub imag:f64}
#[derive(Debug,Clone,Serialize)]
pub struct AcSample{pub frequency_hz:f64,pub values:Vec<ComplexValue>}
/// Reference AC uses source amplitudes from rhs; separate AC source phase is not yet supported.
pub fn ac(plan:&CompiledCircuit,frequencies:&[f64])->SimResult<Vec<AcSample>>{
    if frequencies.is_empty()||frequencies.len()>10_000{return Err(SimError::invalid("AC grid must contain 1..10000 points"));}
    let n=plan.conductance.n;
    let mut output=Vec::with_capacity(frequencies.len());
    for &frequency in frequencies{
        positive(frequency,"frequency_hz")?;
        let omega=std::f64::consts::TAU*frequency;
        let values:Vec<Complex64>=plan.conductance.values.iter().zip(&plan.charge.values).map(|(g,c)|Complex64::new(*g,omega*c)).collect();
        if values.iter().any(|v|!v.re.is_finite()||!v.im.is_finite()){return Err(SimError::invalid("AC matrix overflow"));}
        let matrix=DMatrix::from_row_slice(n,n,&values);
        let rhs=DVector::from_iterator(n,plan.rhs.iter().map(|v|Complex64::new(*v,0.0)));
        let x=matrix.clone().lu().solve(&rhs).ok_or_else(||SimError::new(ErrorCode::SingularMatrix,"singular AC matrix"))?;
        for i in 0..n{
            let residual=(0..n).map(|j|matrix[(i,j)]*x[j]).sum::<Complex64>()-rhs[i];
            let scale=rhs[i].norm()+(0..n).map(|j|(matrix[(i,j)]*x[j]).norm()).sum::<f64>();
            let relative=residual.norm()/scale.max(f64::MIN_POSITIVE);
            if !relative.is_finite()||relative>1e-10{return Err(SimError::new(ErrorCode::NumericalFailure,"AC residual failed"));}
        }
        output.push(AcSample{frequency_hz:frequency,values:x.iter().map(|v|ComplexValue{real:v.re,imag:v.im}).collect()});
    }Ok(output)
}

