//! Minimal integration glue for the pinned external RSpice core; no simulator
//! equations or parser are copied. Reports parse/analysis time separately from
//! process startup and excludes reference generation/comparison from that time.
use rspice_core::{Engine,Netlist};
use rspice_core::engine::{SimulationConfig,SpiceDialect};
use rspice_core::netlist::AnalysisCommand;
use std::{time::Instant,path::Path,fs};
fn main(){if let Err(error)=run(){eprintln!("{error}");std::process::exit(2)}}
fn run()->Result<(),String>{
 let file=std::env::args().nth(1).ok_or("Expected original deck path")?;
 let path=Path::new(&file);let source=fs::read_to_string(path).map_err(|e|e.to_string())?;
 let clock=Instant::now();let netlist=Netlist::parse_with_path(&source,path).map_err(|e|e.to_string())?;let parse_ms=clock.elapsed().as_secs_f64()*1000.;
 let config=SimulationConfig{temperature:300.15,spice_dialect:SpiceDialect::Ngspice,
 integration_method:rspice_core::numerics::integration::IntegrationMethod::Trapezoidal,min_timestep:1e-12,..Default::default()};
 let engine=Engine::new(config);let started=Instant::now();let mut points=0_usize;let mut operations=0_usize;
 for analysis in &netlist.analyses{
  match analysis{
   AnalysisCommand::Op=>{engine.run_dc_op(&netlist).map_err(|e|e.to_string())?;points+=1;},
   AnalysisCommand::Dc{source,start,stop,step,sweep2,restart,..}=>{
    if sweep2.is_some()||*restart{return Err("Nested/restart DC outside probe scope".into());}
    points+=engine.run_dc_sweep(&netlist,source,*start,*stop,*step).map_err(|e|e.to_string())?.len();
   },
   AnalysisCommand::Ac{variation,points:count,start_freq,stop_freq}=>{
    let frequencies=rspice_core::analysis::ac::ac_sweep_frequencies(*variation,*count,*start_freq,*stop_freq);
    if frequencies.is_empty(){return Err("Empty AC sweep".into());}
    points+=engine.run_ac(&netlist,&frequencies).map_err(|e|e.to_string())?.len();
   },
   AnalysisCommand::Tran{step,stop,start,max_step,uic}=>{
    if *uic{return Err("UIC is outside probe scope".into());}
    let ceiling=max_step.unwrap_or(step.min((stop-start.unwrap_or(0.))/50.));
    points+=engine.run_tran(&netlist,*stop,ceiling).map_err(|e|e.to_string())?.time.len();
   },
   _=>return Err("Unsupported requested analysis; no partial timing success".into())
  }
  operations+=1;
 }
 if operations==0{return Err("No requested analyses".into());}
 println!("{{\"state\":\"completed\",\"parse_ms\":{parse_ms},\"analysis_ms\":{},\"operations\":{operations},\"points\":{points}}}",started.elapsed().as_secs_f64()*1000.);
 Ok(())
}
