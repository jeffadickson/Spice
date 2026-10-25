use crate::{circuit::{Circuit,Element},error::{SimError,SimResult},ids::NodeId,linalg::Matrix,model::{CapacitorModel,ResistorModel,TwoTerminalModel}};
/// Immutable G x + d(Q x)/dt = b plan; Q may contain negative flux rows.
#[derive(Debug,Clone)]
pub struct CompiledCircuit{pub(crate)circuit:Circuit,pub(crate)conductance:Matrix,pub(crate)charge:Matrix,pub(crate)rhs:Vec<f64>,pub(crate)branch_ids:Vec<String>}
impl CompiledCircuit{
    pub fn circuit(&self)->&Circuit{&self.circuit}
    pub fn unknown_count(&self)->usize{self.conductance.n}
    pub fn branch_index(&self,id:&str)->Option<usize>{self.branch_ids.iter().position(|v|v==id).map(|i|i+self.circuit.nodes.len()-1)}
}
pub(crate) fn stamp_pair(a:&mut Matrix,p:NodeId,n:NodeId,g:f64){
    if let Some(i)=p.unknown(){a.add(i,i,g);}
    if let Some(i)=n.unknown(){a.add(i,i,g);}
    if let(Some(i),Some(j))=(p.unknown(),n.unknown()){a.add(i,j,-g);a.add(j,i,-g);}
}
pub fn compile(circuit:&Circuit)->SimResult<CompiledCircuit>{
    circuit.validate()?;
    let branches:Vec<String>=circuit.elements.iter().filter_map(|e|match e{Element::Voltage{id,..}|Element::Inductor{id,..}=>Some(id.clone()),_=>None}).collect();
    let dimension=circuit.nodes.len()-1+branches.len();
    let mut a=Matrix::zeros(dimension)?;let mut charge=Matrix::zeros(dimension)?;let mut rhs=vec![0.;dimension];let mut branch=circuit.nodes.len()-1;
    for e in &circuit.elements{match e{
        Element::Capacitor{p,n,farads,..}=>stamp_pair(&mut charge,*p,*n,CapacitorModel(*farads).coefficients()?.capacitance),
        Element::Resistor{p,n,ohms,..}=>stamp_pair(&mut a,*p,*n,ResistorModel(*ohms).coefficients()?.conductance),
        Element::Current{p,n,amps,..}=>{if let Some(i)=p.unknown(){rhs[i]-=amps;}if let Some(i)=n.unknown(){rhs[i]+=amps;}},
        Element::Voltage{p,n,..}|Element::Inductor{p,n,..}=>{
            for(node,sign)in[(*p,1.),(*n,-1.)]{if let Some(i)=node.unknown(){a.add(i,branch,sign);a.add(branch,i,sign);}}
            match e{Element::Voltage{volts,..}=>rhs[branch]=*volts,Element::Inductor{henries,..}=>charge.add(branch,branch,-henries),_=>unreachable!()}
            branch+=1;
        }
    }}
    if a.values.iter().chain(&charge.values).chain(&rhs).any(|v|!v.is_finite()){return Err(SimError::invalid("component values overflowed assembly"));}
    Ok(CompiledCircuit{circuit:circuit.clone(),conductance:a,charge,rhs,branch_ids:branches})
}

