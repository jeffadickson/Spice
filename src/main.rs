use std::io::{self,Read};
use circuit_simulator::{api::{Request,run},error::{ErrorCode,SimError}};
fn execute()->Result<(),SimError>{
    let mut input=String::new();
    io::stdin().take(1_048_577).read_to_string(&mut input).map_err(|e|SimError::invalid(e.to_string()))?;
    if input.len()>1_048_576{return Err(SimError::new(ErrorCode::ResourceExhausted,"input exceeds 1 MiB"));}
    let request:Request=serde_json::from_str(&input).map_err(|e|SimError::invalid(e.to_string()))?;
    let manifest=run(&request)?;
    println!("{}",serde_json::to_string(&manifest).map_err(|e|SimError::new(ErrorCode::IoError,e.to_string()))?);
    Ok(())
}
fn main(){if let Err(error)=execute(){
    println!("{}",serde_json::json!({"schema_version":"1","status":"failed","complete":false,"error":error}));
    std::process::exit(2);
}}

