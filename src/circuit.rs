use crate::{error::{ErrorCode,SimError,SimResult},ids::NodeId};
use serde::{Deserialize,Serialize};
use std::collections::BTreeSet;
/// SI values. Source branch current is positive from p to n.
#[derive(Debug,Clone,Serialize,Deserialize)]
#[serde(tag="kind",rename_all="snake_case",deny_unknown_fields)]
pub enum Element{
    Resistor{id:String,p:NodeId,n:NodeId,ohms:f64},
    Voltage{id:String,p:NodeId,n:NodeId,volts:f64},
}
impl Element{
    pub fn id(&self)->&str{match self{Self::Resistor{id,..}|Self::Voltage{id,..}=>id}}
    pub fn terminals(&self)->(NodeId,NodeId){match self{Self::Resistor{p,n,..}|Self::Voltage{p,n,..}=>(*p,*n)}}
}
/// Ordered node labels are unique; nodes[0] must be "0".
#[derive(Debug,Clone,Serialize,Deserialize)]
#[serde(deny_unknown_fields)]
pub struct Circuit{pub nodes:Vec<String>,pub elements:Vec<Element>}
impl Circuit{
    pub fn validate(&self)->SimResult<()>{
        if self.nodes.first().map(String::as_str)!=Some("0"){return Err(SimError::invalid("nodes[0] must be ground 0"));}
        if self.nodes.len()>128||self.elements.len()>1024{return Err(SimError::new(ErrorCode::ResourceExhausted,"reference circuit limit exceeded"));}
        let mut names=BTreeSet::new();
        for name in &self.nodes{if name.is_empty()||!names.insert(name){return Err(SimError::invalid("node names must be nonempty and unique"));}}
        let mut ids=BTreeSet::new();
        for e in &self.elements{
            if e.id().is_empty()||!ids.insert(e.id()){return Err(SimError::invalid("element IDs must be nonempty and unique"));}
            let(p,n)=e.terminals();
            if p==n||p.0>=self.nodes.len()||n.0>=self.nodes.len(){return Err(SimError::invalid(format!("invalid terminals on {}",e.id())));}
            match e{
                Element::Resistor{ohms,..} if !ohms.is_finite()||*ohms<=0.0=>return Err(SimError::invalid("resistance must be finite and positive")),
                Element::Voltage{volts,..} if !volts.is_finite()=>return Err(SimError::invalid("voltage must be finite")),
                _=>{}
            }
        }Ok(())
    }
}

