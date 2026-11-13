use crate::{
    circuit::{Circuit, Element},
    error::{SimError, SimResult},
    ids::NodeId,
    linalg::Matrix,
    model::{CapacitorModel, DiodeModel, DiodeProfile, ResistorModel, TwoTerminalModel},
    sparse::{CscMatrix, SparseAccumulator},
};
/// Immutable nonlinear current/charge junction, retained separately from linear MNA terms.
#[derive(Debug, Clone)]
pub(crate) struct CompiledDiode {
    pub(crate) id: String,
    pub(crate) p: NodeId,
    pub(crate) n: NodeId,
    pub(crate) model: DiodeProfile,
}
/// Immutable smooth resistance and its differential control terminals.
#[derive(Debug, Clone)]
pub(crate) struct CompiledSwitch {
    pub(crate) p: NodeId,
    pub(crate) n: NodeId,
    pub(crate) cp: NodeId,
    pub(crate) cn: NodeId,
    pub(crate) model: crate::pspice_switch::SwitchModel,
}
/// Immutable four-terminal current-only channel.
#[derive(Debug, Clone)]
pub(crate) struct CompiledMos1 {
    pub(crate) id: String,
    pub(crate) p: NodeId,
    pub(crate) n: NodeId,
    pub(crate) gate: NodeId,
    pub(crate) bulk: NodeId,
    pub(crate) model: crate::mos1::Mos1Model,
}
/// One immutable DC constitutive relation; its output branch is an MNA voltage row.
#[derive(Debug, Clone)]
pub(crate) struct CompiledBehavior {
    pub(crate) branch: usize,
    pub(crate) cp: NodeId,
    pub(crate) cn: NodeId,
    pub(crate) expression: crate::expression::Expr,
}
/// Immutable resistance expression, evaluated at the actual differential bias.
#[derive(Debug, Clone)]
pub(crate) struct CompiledResistance {
    pub(crate) p: NodeId,
    pub(crate) n: NodeId,
    pub(crate) cp: NodeId,
    pub(crate) cn: NodeId,
    pub(crate) expression: crate::expression::Expr,
}
/// Resolved current-control column and immutable voltage-modulation parameters.
#[derive(Debug, Clone)]
pub(crate) struct CompiledScaledCurrent {
    pub(crate) p: NodeId,
    pub(crate) n: NodeId,
    pub(crate) cp: NodeId,
    pub(crate) cn: NodeId,
    pub(crate) control: usize,
    pub(crate) gain: f64,
    pub(crate) voltage_coefficient: f64,
}
/// Shared bounded stamping surface used by compile-time sparse accumulation and trial Jacobians.
pub(crate) trait StampMatrix {
    fn add_stamp(&mut self, row: usize, column: usize, value: f64);
}
impl StampMatrix for Matrix {
    fn add_stamp(&mut self, row: usize, column: usize, value: f64) {
        self.add(row, column, value);
    }
}
impl StampMatrix for SparseAccumulator {
    fn add_stamp(&mut self, row: usize, column: usize, value: f64) {
        self.add(row, column, value);
    }
}
/// Immutable G x + d(Q x)/dt = b plan; Q may contain negative flux rows.
#[derive(Debug, Clone)]
pub struct CompiledCircuit {
    pub(crate) circuit: Circuit,
    pub(crate) conductance: Matrix,
    pub(crate) charge: Matrix,
    /// Canonical directly stamped forms; dense fields above are bounded reference materializations.
    pub(crate) conductance_sparse: CscMatrix,
    pub(crate) charge_sparse: CscMatrix,
    pub(crate) rhs: Vec<f64>,
    pub(crate) branch_ids: Vec<String>,
    pub(crate) diodes: Vec<CompiledDiode>,
    pub(crate) switches: Vec<CompiledSwitch>,
    pub(crate) mos1: Vec<CompiledMos1>,
    pub(crate) behaviors: Vec<CompiledBehavior>,
    pub(crate) resistances: Vec<CompiledResistance>,
    pub(crate) scaled_currents: Vec<CompiledScaledCurrent>,
    pub(crate) bsim330: Vec<([NodeId; 4], crate::bsim330::NativeModel)>,
}
impl CompiledCircuit {
    pub fn circuit(&self) -> &Circuit {
        &self.circuit
    }
    pub fn unknown_count(&self) -> usize {
        self.conductance.n
    }
    /// Canonical conductance stamp pattern for sparse backend analysis.
    pub fn conductance_sparse(&self) -> &CscMatrix {
        &self.conductance_sparse
    }
    /// Canonical charge/flux stamp pattern for sparse transient backend analysis.
    pub fn charge_sparse(&self) -> &CscMatrix {
        &self.charge_sparse
    }
    /// Immutable source vector paired with the compiled conductance plan.
    pub fn rhs(&self) -> &[f64] {
        &self.rhs
    }
    /// Bounded dense reference materialization used for cross-provider verification.
    pub fn conductance_reference(&self) -> &Matrix {
        &self.conductance
    }
    /// Nonlinear device presence selects Newton operating-point solving.
    pub fn has_nonlinear_devices(&self) -> bool {
        !self.diodes.is_empty()
            || !self.behaviors.is_empty()
            || !self.resistances.is_empty()
            || !self.scaled_currents.is_empty()
            || !self.switches.is_empty()
            || !self.mos1.is_empty()
            || !self.bsim330.is_empty()
    }

    pub(crate) fn require_dynamic_native_models(&self) -> SimResult<()> {
        if !self.behaviors.is_empty() {
            return Err(SimError::new(crate::error::ErrorCode::Unsupported,
                "behavioral voltage expressions are currently DC only"));
        }
        if self
            .bsim330
            .iter()
            .any(|(_, model)| matches!(model, crate::bsim330::NativeModel::SoiFd))
        {
            return Err(SimError::new(
                crate::error::ErrorCode::Unsupported,
                "BSIM3 SOI FD provider is static DC only",
            ));
        }
        Ok(())
    }

    pub fn branch_index(&self, id: &str) -> Option<usize> {
        self.branch_ids
            .iter()
            .position(|v| v == id)
            .map(|i| i + self.circuit.nodes.len() - 1)
    }
}
pub(crate) fn stamp_pair(a: &mut impl StampMatrix, p: NodeId, n: NodeId, g: f64) {
    if let Some(i) = p.unknown() {
        a.add_stamp(i, i, g);
    }
    if let Some(i) = n.unknown() {
        a.add_stamp(i, i, g);
    }
    if let (Some(i), Some(j)) = (p.unknown(), n.unknown()) {
        a.add_stamp(i, j, -g);
        a.add_stamp(j, i, -g);
    }
}
pub fn compile(circuit: &Circuit) -> SimResult<CompiledCircuit> {
    circuit.validate()?;
    let branches: Vec<String> = circuit
        .elements
        .iter()
        .filter_map(|e| match e {
            Element::Voltage { id, .. }
            | Element::Inductor { id, .. }
            | Element::Vcvs { id, .. }
            | Element::BehavioralVoltage { id, .. }
            | Element::Ccvs { id, .. } => Some(id.clone()),
            _ => None,
        })
        .collect();
    let dimension = circuit.nodes.len() - 1 + branches.len();
    let mut a = SparseAccumulator::new(dimension)?;
    let mut charge = SparseAccumulator::new(dimension)?;
    let mut rhs = vec![0.; dimension];
    let mut branch = circuit.nodes.len() - 1;
    let mut diodes = Vec::new();
    let mut switches = Vec::new();
    let mut mos1 = Vec::new();
    let mut behaviors = Vec::new();
    let mut resistances = Vec::new();
    let mut scaled_currents = Vec::new();
    let mut bsim330 = Vec::new();
    for e in &circuit.elements {
        if let Some(device) = e.native_mos() {
            bsim330.push(device);
            continue;
        }
        match e {
            Element::VoltageScaledCccs {p,n,cp,cn,control_id,gain,voltage_coefficient,..} => {
                let control=branches.iter().position(|id|id==control_id)
                    .map(|index|index+circuit.nodes.len()-1)
                    .ok_or_else(||SimError::invalid("voltage-scaled CCCS control must name a branch source"))?;
                scaled_currents.push(CompiledScaledCurrent{p:*p,n:*n,cp:*cp,cn:*cn,control,gain:*gain,voltage_coefficient:*voltage_coefficient});
            }
            Element::BehavioralResistor {p,n,cp,cn,expression,..} => resistances.push(CompiledResistance {
                p:*p,n:*n,cp:*cp,cn:*cn,expression:expression.clone(),
            }),
            Element::Bsim330 { .. } | Element::BsimSoiFdReference { .. } => {
                unreachable!("native MOS handled above")
            }
            Element::Mos1Channel {
                id,
                p,
                n,
                gate,
                bulk,
                model,
                ..
            } => mos1.push(CompiledMos1 {
                id: id.clone(),
                p: *p,
                n: *n,
                gate: *gate,
                bulk: *bulk,
                model: *model,
            }),
            Element::JunctionStorage { id, p, n, model } => {
                model.validate()?;
                diodes.push(CompiledDiode {
                    id: id.clone(),
                    p: *p,
                    n: *n,
                    model: DiodeProfile::JunctionStorage(*model),
                });
            }
            Element::SpiceDiode { id, p, n, model } => {
                model.validate()?;
                diodes.push(CompiledDiode {
                    id: id.clone(),
                    p: *p,
                    n: *n,
                    model: DiodeProfile::Spice(*model),
                });
            }
            Element::PspiceSwitch {
                p,
                n,
                cp,
                cn,
                ron,
                roff,
                von,
                voff,
                ..
            } => switches.push(CompiledSwitch {
                p: *p,
                n: *n,
                cp: *cp,
                cn: *cn,
                model: crate::pspice_switch::SwitchModel {
                    ron: *ron,
                    roff: *roff,
                    von: *von,
                    voff: *voff,
                },
            }),
            Element::Capacitor { p, n, farads, .. } => stamp_pair(
                &mut charge,
                *p,
                *n,
                CapacitorModel(*farads).coefficients()?.capacitance,
            ),
            Element::Resistor { p, n, ohms, .. } => stamp_pair(
                &mut a,
                *p,
                *n,
                ResistorModel(*ohms).coefficients()?.conductance,
            ),
            Element::Current { p, n, amps, .. } => {
                if let Some(i) = p.unknown() {
                    rhs[i] -= amps;
                }
                if let Some(i) = n.unknown() {
                    rhs[i] += amps;
                }
            }
            Element::Vccs {
                p,
                n,
                cp,
                cn,
                siemens,
                ..
            } => {
                // KCL receives +g*(Vcp-Vcn) at p and -g*(Vcp-Vcn) at n.
                for (row_node, row_sign) in [(*p, 1.), (*n, -1.)] {
                    for (control_node, control_sign) in [(*cp, 1.), (*cn, -1.)] {
                        if let (Some(row), Some(column)) =
                            (row_node.unknown(), control_node.unknown())
                        {
                            a.add(row, column, row_sign * control_sign * siemens);
                        }
                    }
                }
            }
            Element::Cccs {
                p,
                n,
                control_id,
                gain,
                ..
            } => {
                let control = branches
                    .iter()
                    .position(|id| id == control_id)
                    .map(|index| index + circuit.nodes.len() - 1)
                    .ok_or_else(|| SimError::invalid("CCCS control must name a branch source"))?;
                if let Some(row) = p.unknown() {
                    a.add(row, control, *gain);
                }
                if let Some(row) = n.unknown() {
                    a.add(row, control, -*gain);
                }
            }
            Element::Diode {
                id,
                p,
                n,
                saturation_amps,
                thermal_volts,
                ..
            }
            | Element::DiodeWithCapacitance {
                id,
                p,
                n,
                saturation_amps,
                thermal_volts,
                ..
            } => {
                let conduction = DiodeModel {
                    saturation_amps: *saturation_amps,
                    thermal_volts: *thermal_volts,
                };
                let model = if let Element::DiodeWithCapacitance { farads, .. } = e {
                    stamp_pair(&mut charge, *p, *n, *farads);
                    DiodeProfile::ConstantCharge {
                        conduction,
                        farads: *farads,
                    }
                } else {
                    DiodeProfile::Shockley(conduction)
                };
                model.validate()?;
                diodes.push(CompiledDiode {
                    id: id.clone(),
                    p: *p,
                    n: *n,
                    model,
                });
            }
            Element::Voltage { p, n, .. }
            | Element::Inductor { p, n, .. }
            | Element::Vcvs { p, n, .. }
            | Element::BehavioralVoltage { p, n, .. }
            | Element::Ccvs { p, n, .. } => {
                for (node, sign) in [(*p, 1.), (*n, -1.)] {
                    if let Some(i) = node.unknown() {
                        a.add(i, branch, sign);
                        a.add(branch, i, sign);
                    }
                }
                match e {
                    Element::BehavioralVoltage {cp,cn,expression,..} => {
                        behaviors.push(CompiledBehavior{branch,cp:*cp,cn:*cn,expression:expression.clone()});
                    }
                    Element::Voltage { volts, .. } => rhs[branch] = *volts,
                    Element::Inductor { henries, .. } => charge.add(branch, branch, -henries),
                    Element::Vcvs { cp, cn, gain, .. } => {
                        if let Some(i) = cp.unknown() {
                            a.add(branch, i, -*gain);
                        }
                        if let Some(i) = cn.unknown() {
                            a.add(branch, i, *gain);
                        }
                    }
                    Element::Ccvs {
                        control_id, ohms, ..
                    } => {
                        let control = branches
                            .iter()
                            .position(|id| id == control_id)
                            .map(|index| index + circuit.nodes.len() - 1)
                            .ok_or_else(|| {
                                SimError::invalid("CCVS control must name a branch source")
                            })?;
                        // The branch equation is Vp-Vn-ohms*Icontrol = 0.
                        a.add(branch, control, -*ohms);
                    }
                    _ => unreachable!(),
                }
                branch += 1;
            }
        }
    }
    let conductance_sparse = a.to_csc()?;
    let charge_sparse = charge.to_csc()?;
    if conductance_sparse
        .values()
        .iter()
        .chain(charge_sparse.values())
        .chain(&rhs)
        .any(|v| !v.is_finite())
    {
        return Err(SimError::invalid("component values overflowed assembly"));
    }
    Ok(CompiledCircuit {
        circuit: circuit.clone(),
        conductance: conductance_sparse.to_dense()?,
        charge: charge_sparse.to_dense()?,
        conductance_sparse,
        charge_sparse,
        rhs,
        branch_ids: branches,
        diodes,
        switches,
        mos1,
        behaviors,
        resistances,
        scaled_currents,
        bsim330,
    })
}
