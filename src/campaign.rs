use crate::{api::{Request,Manifest,run_with_registry},error::{ErrorCode,SimError,SimResult},registry::BackendRegistry};
use serde::Serialize;
use std::collections::BTreeSet;
/// Sequential reference campaign; no distributed, resume or retry guarantee is implied.
#[derive(Debug,Serialize)]
pub struct CaseResult{pub case_id:String,pub execution:&'static str,pub result:Option<Manifest>,pub error:Option<SimError>}
pub fn run_cases(requests:&[Request],registry:&BackendRegistry,cancel:&std::sync::atomic::AtomicBool,mut observer:impl FnMut(&CaseResult)->SimResult<()>)->SimResult<()>{
    if requests.is_empty()||requests.len()>64{return Err(SimError::invalid("campaign requires 1..64 cases"));}
    let mut ids=BTreeSet::new();
    for r in requests{if r.case_id.is_empty()||!ids.insert(&r.case_id){return Err(SimError::invalid("campaign case IDs must be unique"));}}
    for request in requests{
        if cancel.load(std::sync::atomic::Ordering::Relaxed){return Err(SimError::new(ErrorCode::Cancelled,"campaign cancelled between cases"));}
        let outcome=match run_with_registry(request,registry){Ok(result)=>CaseResult{case_id:request.case_id.clone(),execution:"succeeded",result:Some(result),error:None},
            Err(error)=>CaseResult{case_id:request.case_id.clone(),execution:"failed",result:None,error:Some(error)}};
        observer(&outcome)?;
    }Ok(())
}

