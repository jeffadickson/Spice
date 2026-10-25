use crate::{api::Manifest,assertion::Outcome,error::{SimError,SimResult},measurement::value_at,transient::Sample};
use serde::{Deserialize,Serialize};
use std::collections::BTreeSet;
#[derive(Debug,Clone,Serialize,Deserialize)]
#[serde(tag="kind",rename_all="snake_case",deny_unknown_fields)]
pub enum Operation{ValueAt{time_s:f64},Minimum{start_s:f64,stop_s:f64},Maximum{start_s:f64,stop_s:f64},Integral{start_s:f64,stop_s:f64},Rms{start_s:f64,stop_s:f64}}
#[derive(Debug,Clone,Serialize,Deserialize)]
#[serde(deny_unknown_fields)]
pub struct MeasurementSpec{pub id:String,pub signal:String,pub operation:Operation}
#[derive(Debug,Clone,Serialize,Deserialize)]
#[serde(deny_unknown_fields)]
pub struct AssertionSpec{pub id:String,pub measurement:String,pub unit:String,pub low:f64,pub high:f64}
#[derive(Debug,Clone,Default,Serialize,Deserialize)]
#[serde(deny_unknown_fields)]
pub struct ExperimentSpec{#[serde(default)]pub measurements:Vec<MeasurementSpec>,#[serde(default)]pub assertions:Vec<AssertionSpec>}
#[derive(Debug,Serialize)]
pub struct MeasurementResult{pub id:String,pub signal:String,pub unit:String,pub status:&'static str,pub value:Option<f64>,pub reason:Option<String>}
#[derive(Debug,Serialize)]
pub struct AssertionResult{pub id:String,pub measurement:String,pub outcome:Outcome,pub reason:Option<String>}
impl ExperimentSpec{
    pub fn validate(&self)->SimResult<()>{
        if self.measurements.len()>256||self.assertions.len()>512{return Err(SimError::invalid("experiment size limit"));}
        let mut ids=BTreeSet::new();
        for m in &self.measurements{
            if m.id.is_empty()||m.id.len()>256||!ids.insert(&m.id){return Err(SimError::invalid("invalid/duplicate measurement ID"));}
            match m.operation{
                Operation::ValueAt{time_s} if !time_s.is_finite()||time_s<0.=>return Err(SimError::invalid("invalid measurement time")),
                Operation::Minimum{start_s,stop_s}|Operation::Maximum{start_s,stop_s}|Operation::Integral{start_s,stop_s}|Operation::Rms{start_s,stop_s}
                    if !start_s.is_finite()||!stop_s.is_finite()||start_s<0.||stop_s<=start_s=>return Err(SimError::invalid("invalid measurement window")),
                _=>{}
            }
        }
        let mut assertions=BTreeSet::new();
        for a in &self.assertions{if a.id.is_empty()||!assertions.insert(&a.id)||!ids.contains(&a.measurement)||!a.low.is_finite()||!a.high.is_finite()||a.low>a.high{return Err(SimError::invalid("invalid assertion bounds/reference/ID"));}}
        Ok(())
    }
}
fn calculate(samples:&[Sample],signal:usize,operation:&Operation)->SimResult<f64>{
    let(start,stop)=match *operation{Operation::ValueAt{time_s}=>return value_at(samples,signal,time_s),
        Operation::Minimum{start_s,stop_s}|Operation::Maximum{start_s,stop_s}|Operation::Integral{start_s,stop_s}|Operation::Rms{start_s,stop_s}=>(start_s,stop_s)};
    let mut points=vec![(start,value_at(samples,signal,start)?)];
    points.extend(samples.iter().filter(|s|s.time_s>start&&s.time_s<stop).map(|s|(s.time_s,s.values[signal])));
    points.push((stop,value_at(samples,signal,stop)?));
    let value=match operation{
        Operation::Minimum{..}=>points.iter().map(|p|p.1).fold(f64::INFINITY,f64::min),
        Operation::Maximum{..}=>points.iter().map(|p|p.1).fold(f64::NEG_INFINITY,f64::max),
        Operation::Integral{..}=>points.windows(2).map(|w|(w[1].0-w[0].0)*(w[0].1/2.+w[1].1/2.)).sum(),
        Operation::Rms{..}=>(points.windows(2).map(|w|(w[1].0-w[0].0)*(w[0].1*w[0].1+w[0].1*w[1].1+w[1].1*w[1].1)/3.).sum::<f64>()/(stop-start)).sqrt(),
        Operation::ValueAt{..}=>unreachable!()
    };crate::quantity::finite(value,"measurement")
}
pub fn evaluate(manifest:&Manifest,spec:&ExperimentSpec)->SimResult<(Vec<MeasurementResult>,Vec<AssertionResult>)>{
    spec.validate()?;let mut measurements=Vec::new();
    for m in &spec.measurements{
        let index=manifest.signals.iter().position(|s|s.id==m.signal);
        let unit=index.map(|i|manifest.signals[i].unit).unwrap_or("");
        let unit=if matches!(m.operation,Operation::Integral{..}){format!("{unit}*s")}else{unit.to_string()};
        let value=match index{Some(i)=>calculate(&manifest.samples,i,&m.operation),None=>Err(SimError::invalid("signal not recorded/available"))};
        let(value,reason)=match value{Ok(v)=>(Some(v),None),Err(e)=>(None,Some(e.message))};
        measurements.push(MeasurementResult{id:m.id.clone(),signal:m.signal.clone(),unit,status:if value.is_some(){"valid"}else{"unavailable"},value,reason});
    }
    let assertions=spec.assertions.iter().map(|a|{
        let m=measurements.iter().find(|m|m.id==a.measurement).expect("validated measurement reference");
        let mismatch=m.unit!=a.unit;
        AssertionResult{id:a.id.clone(),measurement:a.measurement.clone(),outcome:crate::assertion::within(if mismatch{None}else{m.value},a.low,a.high),reason:if mismatch{Some("assertion unit mismatch".into())}else{m.reason.clone()}}
    }).collect();
    Ok((measurements,assertions))
}

