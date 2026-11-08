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
            &serde_json::json!({"schema_version":"1","execution":"bounded_reference","backends":circuit_simulator::registry::BackendRegistry::reference().capabilities(),"analyses":["operating_point","operating_point_newton","ac","transient","transient_configured"],"nonlinear":{"diode_dc":true,"ac_bias_linearization":true,"transient":true,"configured_backward_euler":true,"adaptive":true,"source_overrides":true,"gmin_dc":true},"models":{"mos1_channel":{"conduction":"bounded_shichman_hodges","charge":"explicit_companions_and_optional_meyer","positive_effective_body_bias_with_gamma":false,"intrinsic_meyer":true,"generated_nonzero_tox":false,"generated_levels":[1]},"junction_storage":{"conduction":"none","charge":"spice_depletion_and_diffusion","transport_is_scales_charge_only":true,"configured_trapezoidal":false,"configured_adaptive":false,"configured_explicit_initial_state":false},"spice_diode":{"conduction":"shockley","charge":"spice_depletion_and_diffusion","depletion":true,"transport_charge":true,"avalanche":false,"series_resistance":false,"temperature_scaling":false,"configured_trapezoidal":false,"configured_adaptive":false,"configured_explicit_initial_state":false},"diode":{"conduction":"shockley","charge":"none"},"diode_with_capacitance":{"conduction":"shockley","charge":"constant_q_cv","depletion":false,"transport_charge":false,"configured_trapezoidal":false}},"transport":"one_shot_json","gpu":false}),
        );
    }
    let importing = args == ["--import-ngspice"];
    if !args.is_empty() && !importing {
        return Err(SimError::new(
            ErrorCode::Unsupported,
            "only --capabilities, --import-ngspice or stdin JSON is supported",
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
    if importing {
        // The CLI is an adapter over the same bounded library importer used by
        // callers. Importing never evaluates control commands or simulates.
        return write_json(&circuit_simulator::netlist::import_ngspice_request(
            &input,
            "ngspice-corpus",
        )?);
    }
    let request: Request =
        serde_json::from_str(&input).map_err(|e| SimError::invalid(e.to_string()))?;
    write_json(&run(&request)?)
}
fn main() {
    // The executable owns logging policy. Library callers install their own
    // subscriber; JSON protocol stdout must never contain diagnostic events.
    let _ = tracing_subscriber::fmt()
        .with_env_filter(
            tracing_subscriber::EnvFilter::try_from_default_env()
                .unwrap_or_else(|_| tracing_subscriber::EnvFilter::new("warn")),
        )
        .json()
        .with_writer(io::stderr)
        .with_ansi(false)
        .try_init();
    if let Err(error) = execute() {
        tracing::error!(error_code = ?error.code, "request_failed");
        let _ = write_json(
            &serde_json::json!({"schema_version":"1","status":"failed","complete":false,"error":error}),
        );
        std::process::exit(2);
    }
}
