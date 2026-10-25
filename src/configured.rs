use crate::{sources::SourceOverride,error::{SimError,SimResult},quantity::positive};
use serde::{Deserialize,Serialize};
#[derive(Debug,Clone,Copy,PartialEq,Eq,Default,Serialize,Deserialize)]
#[serde(rename_all="snake_case")]
pub enum Method{#[default]BackwardEuler,Trapezoidal}
/// Advanced linear path uses an operating point at t=0 unless a consistent full MNA state is supplied.
#[derive(Debug,Clone,Serialize,Deserialize)]
#[serde(deny_unknown_fields)]
pub struct TransientConfig{
    pub stop_s:f64,pub max_step_s:f64,pub max_steps:usize,
    #[serde(default)]pub method:Method,
    #[serde(default)]pub initial_state:Option<Vec<f64>>,
    #[serde(default)]pub sources:Vec<SourceOverride>,
}
impl Default for TransientConfig{
    fn default()->Self{Self{stop_s:0.005,max_step_s:1e-5,max_steps:10_000,method:Method::BackwardEuler,initial_state:None,sources:Vec::new()}}
}
impl TransientConfig{
    pub fn validate(&self)->SimResult<()>{
        positive(self.stop_s,"stop_s")?;positive(self.max_step_s,"max_step_s")?;
        if self.max_steps==0||self.max_steps>100_000{return Err(SimError::invalid("configured step budget must be 1..100000"));}
        Ok(())
    }
}

