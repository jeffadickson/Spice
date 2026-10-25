use crate::{
    circuit::{Circuit, Element},
    error::{SimError, SimResult},
    ids::NodeId,
    linalg::Matrix,
    model::{CapacitorModel, DiodeModel, ResistorModel, TwoTerminalModel},
};
/// Immutable nonlinear diode definition retained separately from linear MNA terms.
#[derive(Debug, Clone, Copy)]
pub(crate) struct CompiledDiode {
    pub(crate) p: NodeId,
    pub(crate) n: NodeId,
    pub(crate) model: DiodeModel,
}
/// Immutable G x + d(Q x)/dt = b plan; Q may contain negative flux rows.
#[derive(Debug, Clone)]
pub struct CompiledCircuit {
    pub(crate) circuit: Circuit,
    pub(crate) conductance: Matrix,
    pub(crate) charge: Matrix,
    pub(crate) rhs: Vec<f64>,
    pub(crate) branch_ids: Vec<String>,
    pub(crate) diodes: Vec<CompiledDiode>,
}
impl CompiledCircuit {
    pub fn circuit(&self) -> &Circuit {
        &self.circuit
    }
    pub fn unknown_count(&self) -> usize {
        self.conductance.n
    }
    /// Nonlinear device presence selects Newton operating-point solving.
    pub fn has_nonlinear_devices(&self) -> bool {
        !self.diodes.is_empty()
    }
    pub fn branch_index(&self, id: &str) -> Option<usize> {
        self.branch_ids
            .iter()
            .position(|v| v == id)
            .map(|i| i + self.circuit.nodes.len() - 1)
    }
}
pub(crate) fn stamp_pair(a: &mut Matrix, p: NodeId, n: NodeId, g: f64) {
    if let Some(i) = p.unknown() {
        a.add(i, i, g);
    }
    if let Some(i) = n.unknown() {
        a.add(i, i, g);
    }
    if let (Some(i), Some(j)) = (p.unknown(), n.unknown()) {
        a.add(i, j, -g);
        a.add(j, i, -g);
    }
}
pub fn compile(circuit: &Circuit) -> SimResult<CompiledCircuit> {
    circuit.validate()?;
    let branches: Vec<String> = circuit
        .elements
        .iter()
        .filter_map(|e| match e {
            Element::Voltage { id, .. } | Element::Inductor { id, .. } | Element::Vcvs { id, .. } => Some(id.clone()),
            _ => None,
        })
        .collect();
    let dimension = circuit.nodes.len() - 1 + branches.len();
    let mut a = Matrix::zeros(dimension)?;
    let mut charge = Matrix::zeros(dimension)?;
    let mut rhs = vec![0.; dimension];
    let mut branch = circuit.nodes.len() - 1;
    let mut diodes = Vec::new();
    for e in &circuit.elements {
        match e {
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
            Element::Vccs { p, n, cp, cn, siemens, .. } => {
                // KCL receives +g*(Vcp-Vcn) at p and -g*(Vcp-Vcn) at n.
                for (row_node, row_sign) in [(*p, 1.), (*n, -1.)] {
                    for (control_node, control_sign) in [(*cp, 1.), (*cn, -1.)] {
                        if let (Some(row), Some(column)) = (row_node.unknown(), control_node.unknown()) {
                            a.add(row, column, row_sign * control_sign * siemens);
                        }
                    }
                }
            }
            Element::Cccs { p, n, control_id, gain, .. } => {
                let control = branches.iter().position(|id| id == control_id)
                    .map(|index| index + circuit.nodes.len() - 1)
                    .ok_or_else(|| SimError::invalid("CCCS control must name a branch source"))?;
                if let Some(row) = p.unknown() { a.add(row, control, *gain); }
                if let Some(row) = n.unknown() { a.add(row, control, -*gain); }
            }
            Element::Diode {
                p,
                n,
                saturation_amps,
                thermal_volts,
                ..
            } => diodes.push(CompiledDiode {
                p: *p,
                n: *n,
                model: DiodeModel {
                    saturation_amps: *saturation_amps,
                    thermal_volts: *thermal_volts,
                },
            }),
            Element::Voltage { p, n, .. } | Element::Inductor { p, n, .. } | Element::Vcvs { p, n, .. } => {
                for (node, sign) in [(*p, 1.), (*n, -1.)] {
                    if let Some(i) = node.unknown() {
                        a.add(i, branch, sign);
                        a.add(branch, i, sign);
                    }
                }
                match e {
                    Element::Voltage { volts, .. } => rhs[branch] = *volts,
                    Element::Inductor { henries, .. } => charge.add(branch, branch, -henries),
                    Element::Vcvs { cp, cn, gain, .. } => {
                        if let Some(i) = cp.unknown() { a.add(branch, i, -*gain); }
                        if let Some(i) = cn.unknown() { a.add(branch, i, *gain); }
                    }
                    _ => unreachable!(),
                }
                branch += 1;
            }
        }
    }
    if a.values
        .iter()
        .chain(&charge.values)
        .chain(&rhs)
        .any(|v| !v.is_finite())
    {
        return Err(SimError::invalid("component values overflowed assembly"));
    }
    Ok(CompiledCircuit {
        circuit: circuit.clone(),
        conductance: a,
        charge,
        rhs,
        branch_ids: branches,
        diodes,
    })
}
