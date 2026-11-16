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
    #[cfg(feature = "intel-opencl")]
    if args == ["--intel-device"] {
        let device = spice_intel_host::info().map_err(|e| SimError::new(ErrorCode::Unsupported,e))?;
        return write_json(&serde_json::json!({"device":device,"backend":"intel","experimental":true,"cpu_fallback":false}));
    }
    if args == ["--capabilities"] {
        return write_json(
            &serde_json::json!({"schema_version":"1","execution":"bounded_reference","backends":circuit_simulator::registry::BackendRegistry::reference().capabilities(),"analyses":["operating_point","operating_point_newton","ac","transient","transient_configured"],"nonlinear":{"diode_dc":true,"ac_bias_linearization":true,"transient":true,"configured_backward_euler":true,"adaptive":true,"source_overrides":true,"gmin_dc":true},"models":{"voltage_scaled_cccs":{"dc":true,"ac":true,"factor_offset":true,"transient_qualified":false,"control":"branch_current_and_differential_voltage","charge":"none"},"behavioral_resistor":{"dc":true,"ac":true,"transient_qualified":false,"inputs":1,"maximum_expression_nodes":1024,"maximum_expression_depth":64},"behavioral_voltage":{"dc":true,"ac":false,"transient":true,"transient_profile":"grounded_direct_control_memoryless_backward_euler","cyclic_startup":false,"inputs":1,"maximum_expression_nodes":1024,"maximum_expression_depth":64},"mos1_channel":{"conduction":"bounded_shichman_hodges","charge":"explicit_companions_and_optional_meyer","positive_effective_body_bias_with_gamma":true,"forward_body_newton":"exact_piecewise","forward_body_ac":"ngspice_convention","intrinsic_meyer":true,"generated_nonzero_tox":true,"generated_levels":[1]},"junction_storage":{"conduction":"none","charge":"spice_depletion_and_diffusion","transport_is_scales_charge_only":true,"forward_early_coupled_charge":true,"configured_trapezoidal":true,"configured_adaptive":true,"configured_explicit_initial_state":false},"spice_diode":{"conduction":"shockley","charge":"spice_depletion_and_diffusion","depletion":true,"transport_charge":true,"avalanche":false,"series_resistance":false,"temperature_scaling":false,"configured_trapezoidal":true,"configured_adaptive":true,"configured_explicit_initial_state":false},"diode":{"conduction":"shockley","charge":"none"},"diode_with_capacitance":{"conduction":"shockley","charge":"constant_q_cv","depletion":false,"transport_charge":false,"configured_trapezoidal":true}},"transport":"one_shot_json","gpu":cfg!(feature="intel-opencl")}),
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
    let execution = execute();
    #[cfg(feature = "intel-opencl")]
    if let Err(message) = spice_intel_host::shutdown() {
        tracing::error!(%message,"intel_shutdown_failed");
        std::process::exit(2);
    }
    if let Err(error) = execution {
        tracing::error!(error_code = ?error.code, "request_failed");
        let _ = write_json(
            &serde_json::json!({"schema_version":"1","status":"failed","complete":false,"error":error}),
        );
        std::process::exit(2);
    }
}
