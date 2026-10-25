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
    },
    TransientConfigured {
        options: crate::configured::TransientConfig,
    },
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
    let provider = registry.get(&request.backend)?;
    if let Some(experiment) = &request.experiment {
        experiment.validate()?;
    }
    let solver: &dyn LinearSolver = provider.as_ref();
    let plan = compile(&request.circuit)?;
    let mut integration = None;
    let mut newton = None;
    let samples = match &request.analysis {
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
    let ac_samples = match &request.analysis {
        Analysis::Ac { frequencies_hz } => crate::ac::ac(&plan, frequencies_hz, solver)?,
        _ => Vec::new(),
    };
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
    let dc_power = if matches!(
        request.analysis,
        Analysis::OperatingPoint | Analysis::OperatingPointNewton { .. }
    ) {
        Some(crate::power::dc_power(&plan, &samples[0].values)?)
    } else {
        None
    };
    let mut manifest = Manifest {
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
    Ok(manifest)
}
