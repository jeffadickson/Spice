use crate::{
    circuit::Circuit,
    compile::compile,
    dc::{operating_point_detailed, operating_point_with_options},
    error::{SimError, SimResult},
    linalg::{BackendCapabilities, LinearSolver},
    transient::{Sample, TransientOptions, transient},
};
use serde::{Deserialize, Serialize};
#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(tag = "kind", rename_all = "snake_case", deny_unknown_fields)]
pub enum Analysis {
    OperatingPoint,
    OperatingPointNewton {
        options: crate::newton::NewtonOptions,
    },
    Transient {
        options: TransientOptions,
    },
    Ac {
        frequencies_hz: Vec<f64>,
        /// Optional source phasors replace the nominal real AC excitation.
        #[serde(default)]
        source_phasors: Vec<AcSourcePhasor>,
    },
    TransientConfigured {
        options: crate::configured::TransientConfig,
    },
}

/// A finite complex small-signal source value using the circuit's p-to-n sign.
#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(deny_unknown_fields)]
pub struct AcSourcePhasor {
    pub id: String,
    pub real: f64,
    pub imag: f64,
}
#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(deny_unknown_fields)]
pub struct Request {
    pub schema_version: String,
    pub case_id: String,
    pub circuit: Circuit,
    pub analysis: Analysis,
    #[serde(default = "default_backend")]
    pub backend: String,
    #[serde(default)]
    pub experiment: Option<crate::experiment::ExperimentSpec>,
}
fn default_backend() -> String {
    "lu".into()
}
#[derive(Debug, Serialize)]
pub struct Signal {
    pub id: String,
    pub quantity: &'static str,
    pub unit: &'static str,
}
#[derive(Debug, Serialize)]
pub struct Manifest {
    /// Independent physical checks; unavailable checks are explicit, never inferred passes.
    pub consistency: crate::audit::AuditReport,
    pub diagnostics: Vec<crate::topology::Diagnostic>,
    pub schema_version: &'static str,
    pub case_id: String,
    pub status: &'static str,
    pub complete: bool,
    pub backend: BackendCapabilities,
    pub request_sha256: String,
    pub signals: Vec<Signal>,
    pub samples: Vec<Sample>,
    pub ac_samples: Vec<crate::ac::AcSample>,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub integration: Option<crate::configured::IntegrationReport>,
    pub measurements: Vec<crate::experiment::MeasurementResult>,
    pub assertions: Vec<crate::experiment::AssertionResult>,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub dc_power: Option<crate::power::PowerReport>,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub newton: Option<crate::newton::NewtonReport>,
}
/// Immutable request validation precedes compilation. Only explicit CPU reference choices exist.
pub fn run(request: &Request) -> SimResult<Manifest> {
    run_with_registry(request, &crate::registry::BackendRegistry::reference())
}
/// Callers can register providers without changing circuit representations.
pub fn run_with_registry(
    request: &Request,
    registry: &crate::registry::BackendRegistry,
) -> SimResult<Manifest> {
    if request.schema_version != "1" || request.case_id.is_empty() || request.case_id.len() > 256 {
        return Err(SimError::invalid("unsupported schema or invalid case_id"));
    }
    let span =
        tracing::info_span!("simulation", case_id = %request.case_id, backend = %request.backend);
    let _entered = span.enter();
    tracing::debug!("request_validated");
    let provider = registry.get(&request.backend)?;
    if let Some(experiment) = &request.experiment {
        experiment.validate()?;
    }
    let solver: &dyn LinearSolver = provider.as_ref();
    let diagnostics = crate::topology::inspect(&request.circuit)?;
    if let Some(diagnostic) = diagnostics.iter().find(|d| d.severity == "error") {
        return Err(SimError::invalid(format!(
            "{}: {}",
            diagnostic.code, diagnostic.subject
        )));
    }
    for diagnostic in &diagnostics {
        tracing::warn!(code = diagnostic.code, subject = %diagnostic.subject, "topology_diagnostic");
    }
    let plan = compile(&request.circuit)?;
    tracing::debug!(unknowns = plan.unknown_count(), "circuit_compiled");
    let mut integration = None;
    let mut newton = None;
    let mut samples = match &request.analysis {
        Analysis::OperatingPoint => {
            let (values, report) = operating_point_detailed(&plan, solver)?;
            newton = report;
            vec![Sample {
                time_s: 0.0,
                values,
            }]
        }
        Analysis::OperatingPointNewton { options } => {
            let (values, report) = operating_point_with_options(&plan, solver, *options)?;
            newton = report;
            vec![Sample {
                time_s: 0.0,
                values,
            }]
        }
        Analysis::Transient { options } => transient(&plan, solver, options)?,
        Analysis::Ac { .. } => Vec::new(),
        Analysis::TransientConfigured { options } => {
            let result = crate::configured::run(&plan, solver, options, &false.into())?;
            integration = Some(result.report);
            result.samples
        }
    };
    let mut ac_samples = match &request.analysis {
        Analysis::Ac {
            frequencies_hz,
            source_phasors,
        } => crate::ac::ac_with_source_phasors(&plan, frequencies_hz, source_phasors, solver)?,
        _ => Vec::new(),
    };
    // Audit raw MNA states before adding derived public signals. This layer uses
    // element equations, not the matrices whose assembly it is checking.
    let options = crate::audit::AuditOptions::default();
    let mut consistency = match &request.analysis {
        Analysis::OperatingPoint | Analysis::OperatingPointNewton { .. } => crate::audit::dc(
            &request.circuit,
            &samples[0].values,
            options,
            newton.as_ref().and_then(|r| r.gmin_siemens),
        )?,
        Analysis::Transient { .. } => {
            let mut report = crate::audit::AuditReport { checks: Vec::new() };
            let mut previous = vec![0.; plan.unknown_count()];
            let mut time = 0.;
            for sample in &samples {
                let mut step = crate::audit::backward_euler_step(
                    &request.circuit,
                    &previous,
                    &sample.values,
                    sample.time_s - time,
                    options,
                )?;
                for check in &mut step.checks {
                    check.reason = Some(format!(
                        "{}; accepted time_s={}",
                        check.reason.as_deref().unwrap_or("independent step audit"),
                        sample.time_s
                    ));
                }
                report.merge_worst(step);
                previous.clone_from(&sample.values);
                time = sample.time_s;
            }
            report
        }
        _ => crate::audit::AuditReport {
            checks: vec![crate::audit::Check::not_checked(
                "independent_physical_audit",
                "",
                "AC and configured transient physical audits are outside this bounded profile; existing numerical residual checks still apply",
            )],
        },
    };
    consistency.checks.push(crate::audit::Check::not_checked("global_accuracy", "", "physical consistency does not bound global error; refinement and independent reference evidence are required"));
    if let Some(check) = consistency
        .checks
        .iter()
        .find(|c| c.status == crate::audit::CheckStatus::Failed)
    {
        return Err(SimError::new(
            crate::error::ErrorCode::NumericalFailure,
            format!(
                "independent audit failed {}: discrepancy {:?} {}, tolerance {:?}",
                check.id, check.discrepancy, check.unit, check.tolerance
            ),
        ));
    }
    tracing::debug!(
        checks = consistency.checks.len(),
        "physical_audit_completed"
    );
    let mut signals: Vec<Signal> = request
        .circuit
        .nodes
        .iter()
        .skip(1)
        .map(|name| Signal {
            id: format!("v({name})"),
            quantity: "voltage",
            unit: "V",
        })
        .collect();
    signals.extend(plan.branch_ids.iter().map(|id| Signal {
        id: format!("i({id})"),
        quantity: "current",
        unit: "A",
    }));
    // Diodes do not introduce an MNA branch unknown. Publish their derived
    // p-to-n currents so observation/measurement contracts can address them.
    for diode in &plan.diodes {
        let model = diode.model;
        // Composite profiles expose conduction explicitly. A transient initial
        // state does not generally determine dV/dt, so a total device current
        // must not be invented for a constant-charge profile.
        let signal_id = if model.capacitance().is_some() {
            format!("i_conduction({})", diode.id)
        } else {
            format!("i({})", diode.id)
        };
        signals.push(Signal {
            id: signal_id,
            quantity: "current",
            unit: "A",
        });
        for sample in &mut samples {
            let voltage = diode.p.unknown().map(|i| sample.values[i]).unwrap_or(0.)
                - diode.n.unknown().map(|i| sample.values[i]).unwrap_or(0.);
            sample
                .values
                .push(diode.model.linearize(voltage)?.current_amps);
        }
        if !ac_samples.is_empty() {
            let bias = crate::dc::operating_point(&plan, solver)?;
            let voltage = diode.p.unknown().map(|i| bias[i]).unwrap_or(0.)
                - diode.n.unknown().map(|i| bias[i]).unwrap_or(0.);
            let conductance = diode.model.linearize(voltage)?.conductance_siemens;
            for sample in &mut ac_samples {
                let p = diode.p.unknown().map(|i| &sample.values[i]);
                let n = diode.n.unknown().map(|i| &sample.values[i]);
                let real = conductance
                    * (p.map(|v| v.real).unwrap_or(0.) - n.map(|v| v.real).unwrap_or(0.));
                let imag = conductance
                    * (p.map(|v| v.imag).unwrap_or(0.) - n.map(|v| v.imag).unwrap_or(0.));
                sample.values.push(crate::ac::ComplexValue { real, imag });
            }
        }
    }
    let dc_power = if matches!(
        request.analysis,
        Analysis::OperatingPoint | Analysis::OperatingPointNewton { .. }
    ) {
        Some(crate::power::dc_power(
            &plan,
            &samples[0].values[..plan.unknown_count()],
        )?)
    } else {
        None
    };
    let mut manifest = Manifest {
        consistency,
        diagnostics,
        schema_version: "1",
        case_id: request.case_id.clone(),
        status: "succeeded",
        complete: true,
        backend: solver.capabilities(),
        request_sha256: crate::provenance::request_hash(request)?,
        signals,
        samples,
        ac_samples,
        integration,
        dc_power,
        newton,
        measurements: Vec::new(),
        assertions: Vec::new(),
    };
    if let Some(experiment) = &request.experiment {
        let (m, a) = crate::experiment::evaluate(&manifest, experiment)?;
        manifest.measurements = m;
        manifest.assertions = a;
    }
    tracing::info!(
        samples = manifest.samples.len(),
        ac_samples = manifest.ac_samples.len(),
        "simulation_completed"
    );
    Ok(manifest)
}
