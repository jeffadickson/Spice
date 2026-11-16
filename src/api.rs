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
    if plan
        .diodes
        .iter()
        .any(|d| matches!(d.model,crate::model::DiodeProfile::Spice(m)|crate::model::DiodeProfile::JunctionStorage(m) if m.has_charge()))
        && matches!(&request.analysis,Analysis::TransientConfigured { options } if options.initial_state.is_some())
    {
        return Err(crate::error::SimError::new(
            crate::error::ErrorCode::Unsupported,
            "charged junction total current requires integration initialized from operating point",
        ));
    }
    let memoryless_behavior = matches!(&request.analysis,Analysis::TransientConfigured{options}
        if options.method==crate::configured::Method::BackwardEuler && options.initial_state.is_none())
        && plan.memoryless_behavioral_profile();
    if !plan.behaviors.is_empty() && !matches!(request.analysis,Analysis::OperatingPoint|Analysis::OperatingPointNewton{..}) && !memoryless_behavior {
        return Err(SimError::new(crate::error::ErrorCode::Unsupported,"behavioral voltage dynamic profile requires memoryless grounded outputs and backward Euler; AC and storage remain unqualified"));
    }
    tracing::debug!(unknowns = plan.unknown_count(), "circuit_compiled");
    let mut integration = None;
    let mut midpoint_history = Vec::new();
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
            midpoint_history = result.midpoint_history;
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
        Analysis::Ac { source_phasors, .. } => {
            let mut report = crate::audit::AuditReport { checks: Vec::new() };
            for sample in &ac_samples {
                let mut point =
                    crate::audit::ac(&request.circuit, sample, source_phasors, options)?;
                for check in &mut point.checks {
                    check.reason = Some(format!(
                        "{}; frequency_hz={}",
                        check.reason.as_deref().unwrap_or("independent AC audit"),
                        sample.frequency_hz
                    ));
                }
                report.merge_worst(point);
            }
            report
        }
        Analysis::TransientConfigured { options: config } => {
            let mut report = crate::audit::AuditReport { checks: Vec::new() };
            for (index,pair) in samples.windows(2).enumerate() {
                let midpoint = midpoint_history.get(index).and_then(Option::as_ref);
                let intervals: Vec<_> = if let Some(mid) = midpoint {
                    vec![(&pair[0],mid),(mid,&pair[1])]
                } else { vec![(&pair[0],&pair[1])] };
                for (before,after) in intervals {
                let mut actual_config = config.clone();
                if midpoint.is_some() { actual_config.adaptive=None; }
                if integration.as_ref().is_some_and(|r: &crate::configured::IntegrationReport|
                    r.backward_euler_restarts.binary_search_by(|t|t.total_cmp(&after.time_s)).is_ok()) {
                    actual_config.method = crate::configured::Method::BackwardEuler;
                }
                let mut step = crate::audit::configured_step(
                    &request.circuit,
                    before,
                    after,
                    &actual_config,
                    options,
                )?;
                for check in &mut step.checks {
                    check.reason = Some(format!(
                        "{}; accepted time_s={}",
                        check
                            .reason
                            .as_deref()
                            .unwrap_or("independent configured audit"),
                        after.time_s
                    ));
                }
                report.merge_worst(step);
                }
            }
            report
        }
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
        let signal_id = if model.has_charge() {
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
        // The SPICE junction exposes both total terminal current and conduction.
        // Raw BE starts with the documented zero MNA state; configured BE has
        // an operating-point sample at t=0. Rejected trials are absent here.
        if matches!(
            model,
            crate::model::DiodeProfile::Spice(_) | crate::model::DiodeProfile::JunctionStorage(_)
        ) && model.has_charge()
        {
            signals.push(Signal {
                id: format!("i({})", diode.id),
                quantity: "current",
                unit: "A",
            });
            let mut old_q = 0.;
            let mut old_time = 0.;
            let mut old_displacement = 0.;
            for (index,sample) in samples.iter_mut().enumerate() {
                let v = diode.p.unknown().map(|i| sample.values[i]).unwrap_or(0.)
                    - diode.n.unknown().map(|i| sample.values[i]).unwrap_or(0.);
                let q = diode.charge(&sample.values)?
                    .ok_or_else(|| SimError::invalid("missing SPICE charge"))?
                    .charge_coulombs;
                let conduction = model.linearize(v)?.current_amps;
                let dynamic = matches!(
                    request.analysis,
                    Analysis::Transient { .. } | Analysis::TransientConfigured { .. }
                );
                let displacement = if dynamic && sample.time_s > old_time {
                    let at_endpoint = |charge:f64, old_charge:f64, time:f64, before:f64, old_current:f64| {
                        let average=(charge-old_charge)/(time-before);
                        if matches!(&request.analysis,Analysis::TransientConfigured{options} if options.method==crate::configured::Method::Trapezoidal)
                            && !integration.as_ref().is_some_and(|r|r.backward_euler_restarts.binary_search_by(|t|t.total_cmp(&time)).is_ok()) {
                            2.*average-old_current
                        } else {average}
                    };
                    if let Some(mid) = index.checked_sub(1).and_then(|i|midpoint_history.get(i)).and_then(Option::as_ref) {
                        let mq=diode.charge(&mid.values)?.ok_or_else(||SimError::invalid("missing midpoint junction charge"))?.charge_coulombs;
                        let mi=at_endpoint(mq,old_q,mid.time_s,old_time,old_displacement);
                        at_endpoint(q,mq,sample.time_s,mid.time_s,mi)
                    } else {at_endpoint(q,old_q,sample.time_s,old_time,old_displacement)}
                } else {
                    0.
                };
                let total = conduction + displacement;
                if !total.is_finite() {
                    return Err(SimError::invalid("nonfinite total diode current"));
                }
                sample.values.push(total);
                old_q = q;
                old_time = sample.time_s;
                old_displacement = displacement;
            }
            if !ac_samples.is_empty() {
                let bias = crate::dc::operating_point(&plan, solver)?;
                let v = diode.p.unknown().map(|i| bias[i]).unwrap_or(0.)
                    - diode.n.unknown().map(|i| bias[i]).unwrap_or(0.);
                let g = model.linearize(v)?.conductance_siemens;
                let q=diode.charge(&bias)?.ok_or_else(|| SimError::invalid("missing SPICE charge"))?;
                let c=q.capacitance_farads;
                for sample in &mut ac_samples {
                    let real = diode
                        .p
                        .unknown()
                        .map(|i| sample.values[i].real)
                        .unwrap_or(0.)
                        - diode
                            .n
                            .unknown()
                            .map(|i| sample.values[i].real)
                            .unwrap_or(0.);
                    let imag = diode
                        .p
                        .unknown()
                        .map(|i| sample.values[i].imag)
                        .unwrap_or(0.)
                        - diode
                            .n
                            .unknown()
                            .map(|i| sample.values[i].imag)
                            .unwrap_or(0.);
                    let wc = std::f64::consts::TAU * sample.frequency_hz * c;
                    let (cr,ci)=if let Some(control)=diode.early_effect {
                        let delta=|p:crate::ids::NodeId,n:crate::ids::NodeId,imag:bool|{
                            let at=|node:crate::ids::NodeId|node.unknown().map_or(0.,|i|if imag{sample.values[i].imag}else{sample.values[i].real});
                            at(p)-at(n)
                        };
                        let w=std::f64::consts::TAU*sample.frequency_hz*q.control_farads;
                        (-w*delta(control.cp,control.cn,true),w*delta(control.cp,control.cn,false))
                    } else {(0.,0.)};
                    sample.values.push(crate::ac::ComplexValue {
                        real: g * real - wc * imag+cr,
                        imag: g * imag + wc * real+ci,
                    });
                }
            }
        }
    }
    // This observable is channel conduction only; Meyer displacement uses separate gate branches.
    // Generated M cards retain their outer branch as the full drain observable.
    for channel in &plan.mos1 {
        signals.push(Signal {
            id: format!("i_channel({})", channel.id),
            quantity: "current",
            unit: "A",
        });
        let nodes = [channel.p, channel.gate, channel.n, channel.bulk];
        for sample in &mut samples {
            sample.values.push(
                channel
                    .model
                    .linearize(
                        nodes.map(|node| node.unknown().map(|i| sample.values[i]).unwrap_or(0.)),
                    )?
                    .current_amps,
            );
        }
        if !ac_samples.is_empty() {
            let bias = crate::dc::operating_point(&plan, solver)?;
            let partials = channel
                .model
                .spice_ac_linearize(nodes.map(|node| node.unknown().map(|i| bias[i]).unwrap_or(0.)))?
                .partials_siemens;
            for sample in &mut ac_samples {
                let mut real = 0.;
                let mut imag = 0.;
                for (node, partial) in nodes.into_iter().zip(partials) {
                    if let Some(i) = node.unknown() {
                        real += partial * sample.values[i].real;
                        imag += partial * sample.values[i].imag;
                    }
                }
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
