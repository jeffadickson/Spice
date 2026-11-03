//! Freeze one AC matrix using public model/compile APIs for independent provider
//! regression. This tool never adjusts residual acceptance or executes controls.
use circuit_simulator::{
    api::{Analysis, Request},
    circuit::Element,
    compile::compile,
    dc, newton,
    registry::BackendRegistry,
    solver_contract::SparseComplexSystem,
};
use num_complex::Complex64;
use sha2::{Digest, Sha256};
fn main() -> Result<(), Box<dyn std::error::Error>> {
    let args: Vec<_> = std::env::args().skip(1).collect();
    if args.len() != 1 {
        return Err("expected one owned request path".into());
    }
    let bytes = std::fs::read(&args[0])?;
    let request: Request = serde_json::from_slice(&bytes)?;
    let Analysis::Ac {
        frequencies_hz,
        source_phasors,
    } = &request.analysis
    else {
        return Err("expected AC request".into());
    };
    if frequencies_hz.len() != 1 {
        return Err("expected exactly one frequency".into());
    }
    let plan = compile(&request.circuit)?;
    let registry = BackendRegistry::reference();
    let provider = registry.get(&request.backend)?;
    let bias = dc::operating_point(&plan, provider.as_ref())?;
    let g = newton::conductance_at_bias_csc(&plan, &bias)?;
    let c = newton::charge_at_bias_csc(&plan, &bias)?;
    let matrix =
        SparseComplexSystem::from_real_parts(&g, &c, std::f64::consts::TAU * frequencies_hz[0])?;
    let mut rhs = vec![Complex64::new(0., 0.); plan.unknown_count()];
    for e in &request.circuit.elements {
        match e {
            Element::Voltage { id, volts, .. } => {
                let a = source_phasors
                    .iter()
                    .find(|p| p.id == *id)
                    .map(|p| Complex64::new(p.real, p.imag))
                    .unwrap_or(Complex64::new(*volts, 0.));
                rhs[plan.branch_index(id).ok_or("missing source branch")?] = a;
            }
            Element::Current { id, p, n, amps } => {
                let a = source_phasors
                    .iter()
                    .find(|v| v.id == *id)
                    .map(|p| Complex64::new(p.real, p.imag))
                    .unwrap_or(Complex64::new(*amps, 0.));
                if let Some(i) = p.unknown() {
                    rhs[i] -= a;
                }
                if let Some(i) = n.unknown() {
                    rhs[i] += a;
                }
            }
            _ => {}
        }
    }
    let entries: Vec<_> = matrix
        .pattern()
        .slots()
        .zip(matrix.values())
        .map(|((r, c), v)| serde_json::json!([r, c, v.re, v.im]))
        .collect();
    let rhs: Vec<_> = rhs.iter().map(|v| [v.re, v.im]).collect();
    println!(
        "{}",
        serde_json::to_string_pretty(
            &serde_json::json!({"request_sha256":format!("{:x}",Sha256::digest(&bytes)),"backend_for_bias":request.backend,"frequency_hz":frequencies_hz[0],"dimension":plan.unknown_count(),"bias":bias,"entries":entries,"rhs":rhs})
        )?
    );
    Ok(())
}
