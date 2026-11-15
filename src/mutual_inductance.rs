//! Linear signed flux linkage. Coupling carries no external electrical port.
use crate::{circuit::{Circuit,Element},error::{SimError,SimResult}};
/// Resolve winding identities and compute M in henries without overflowing L1*L2.
pub fn pairs(c:&Circuit)->SimResult<Vec<(&str,&str,f64)>> {
 let mut result=Vec::new();
 for e in &c.elements {if let Element::MutualInductance{first,second,coefficient,p,n,..}=e {
  if p.0!=0||n.0!=0||first==second||!coefficient.is_finite()||coefficient.abs()>1. {return Err(SimError::invalid("invalid mutual coupling coefficient/reference"));}
  let l=|id:&str|c.elements.iter().find_map(|e|match e{Element::Inductor{id:name,henries,..}if name==id=>Some(*henries),_=>None}).ok_or_else(||SimError::invalid("mutual coupling must reference existing inductors"));
  let (a,b)=(l(first)?,l(second)?);let m=coefficient*(a.sqrt()*b.sqrt());
  if !m.is_finite()||(*coefficient!=0.&&m==0.){return Err(SimError::invalid("mutual inductance overflow"));}result.push((first.as_str(),second.as_str(),m));
 }}Ok(result)
}
/// Validate the normalized inductance matrix, including signed multi-pair energy.
/// Semidefinite perfect coupling is permitted; negative-energy groups refuse.
pub fn validate(c:&Circuit)->SimResult<()> {
 let p=pairs(c)?;if p.is_empty(){return Ok(())}
 let ls:Vec<_>=c.elements.iter().filter_map(|e|match e{Element::Inductor{id,henries,..}=>Some((id.as_str(),*henries)),_=>None}).collect();
 let n=ls.len();let mut a=vec![vec![0.;n];n];for i in 0..n{a[i][i]=1.;}
 for e in &c.elements {if let Element::MutualInductance{first,second,coefficient,..}=e {let i=ls.iter().position(|x|x.0==first).unwrap();let j=ls.iter().position(|x|x.0==second).unwrap();a[i][j]+=coefficient;a[j][i]+=coefficient;}}
 // Pivoted Schur complements avoid dividing a nearly zero perfect-coupling pivot.
 for k in 0..n{let pivot=(k..n).max_by(|&i,&j|a[i][i].total_cmp(&a[j][j])).unwrap();a.swap(k,pivot);for row in &mut a{row.swap(k,pivot)}
  let d=a[k][k];let tolerance=1e-12*n as f64;
  if d < -tolerance{return Err(SimError::invalid("nonpassive mutual inductance matrix"));}
  if d<=tolerance{if (k..n).any(|i|(k..n).any(|j|a[i][j].abs()>tolerance)){return Err(SimError::invalid("nonpassive singular mutual inductance matrix"));}break;}
  for i in k+1..n{for j in k+1..n{a[i][j]-=a[i][k]*a[k][j]/d;}}
 }Ok(())
}
