use circuit_simulator::{
    api::{Request, run},
    error::{ErrorCode, SimError},
};
use std::io::{self, Read, Write};
fn write_json(value: &impl serde::Serialize) -> Result<(), SimError> {
    let mut out = io::stdout().lock();
    serde_json::to_writer(&mut out, value)
        .map_err(|e| SimError::new(ErrorCode::IoError, e.to_string()))?;
    out.write_all(b"\n")
        .map_err(|e| SimError::new(ErrorCode::IoError, e.to_string()))
}
fn execute() -> Result<(), SimError> {
    let args: Vec<_> = std::env::args().skip(1).collect();
    if args == ["--capabilities"] {
        return write_json(
            &serde_json::json!({"schema_version":"1","execution":"bounded_reference","backends":circuit_simulator::registry::BackendRegistry::reference().capabilities(),"analyses":["operating_point","ac","transient","transient_configured"],"transport":"one_shot_json","gpu":false}),
        );
    }
    if !args.is_empty() {
        return Err(SimError::new(
            ErrorCode::Unsupported,
            "only --capabilities or stdin JSON is supported",
        ));
    }
    let mut input = String::new();
    io::stdin()
        .take(1_048_577)
        .read_to_string(&mut input)
        .map_err(|e| SimError::invalid(e.to_string()))?;
    if input.len() > 1_048_576 {
        return Err(SimError::new(
            ErrorCode::ResourceExhausted,
            "input exceeds 1 MiB",
        ));
    }
    let request: Request =
        serde_json::from_str(&input).map_err(|e| SimError::invalid(e.to_string()))?;
    write_json(&run(&request)?)
}
fn main() {
    if let Err(error) = execute() {
        let _ = write_json(
            &serde_json::json!({"schema_version":"1","status":"failed","complete":false,"error":error}),
        );
        std::process::exit(2);
    }
}
