use crate::{circuit::Circuit,compile::compile,dc::operating_point,error::{SimError,SimResult},linalg::{BackendCapabilities,LinearSolver},transient::{Sample,TransientOptions,transient}};
use serde::{Deserialize,Serialize};
#[derive(Debug,Clone,Serialize,Deserialize)]
#[serde(tag="kind",rename_all="snake_case",deny_unknown_fields)]
pub enum Analysis{OperatingPoint,Transient{options:TransientOptions},Ac{frequencies_hz:Vec<f64>}}
#[derive(Debug,Clone,Serialize,Deserialize)]
#[serde(deny_unknown_fields)]
pub struct Request{pub schema_version:String,pub case_id:String,pub circuit:Circuit,pub analysis:Analysis,#[serde(default="default_backend")]pub backend:String}
fn default_backend()->String{"lu".into()}
#[derive(Debug,Serialize)]
pub struct Signal{pub id:String,pub quantity:&'static str,pub unit:&'static str}
#[derive(Debug,Serialize)]
pub struct Manifest{pub schema_version:&'static str,pub case_id:String,pub status:&'static str,pub complete:bool,pub backend:BackendCapabilities,pub request_sha256:String,pub signals:Vec<Signal>,pub samples:Vec<Sample>,pub ac_samples:Vec<crate::ac::AcSample>}
/// Immutable request validation precedes compilation. Only explicit CPU reference choices exist.
pub fn run(request:&Request)->SimResult<Manifest>{
    run_with_registry(request,&crate::registry::BackendRegistry::reference())
}
/// Callers can register providers without changing circuit representations.
pub fn run_with_registry(request:&Request,registry:&crate::registry::BackendRegistry)->SimResult<Manifest>{
    if request.schema_version!="1"||request.case_id.is_empty()||request.case_id.len()>256{return Err(SimError::invalid("unsupported schema or invalid case_id"));}
    let provider=registry.get(&request.backend)?;
    let solver:&dyn LinearSolver=provider.as_ref();
    let plan=compile(&request.circuit)?;
    let samples=match &request.analysis{
        Analysis::OperatingPoint=>vec![Sample{time_s:0.0,values:operating_point(&plan,solver)?}],
        Analysis::Transient{options}=>transient(&plan,solver,options)?
        ,Analysis::Ac{..}=>Vec::new()
    };
    let ac_samples=match &request.analysis{Analysis::Ac{frequencies_hz}=>{
        crate::ac::ac(&plan,frequencies_hz,solver)?
    },_=>Vec::new()};
    let mut signals:Vec<Signal>=request.circuit.nodes.iter().skip(1).map(|name|Signal{id:format!("v({name})"),quantity:"voltage",unit:"V"}).collect();
    signals.extend(plan.voltage_branches.iter().map(|id|Signal{id:format!("i({id})"),quantity:"current",unit:"A"}));
    Ok(Manifest{schema_version:"1",case_id:request.case_id.clone(),status:"succeeded",complete:true,backend:solver.capabilities(),request_sha256:crate::provenance::request_hash(request)?,signals,samples,ac_samples})
}

