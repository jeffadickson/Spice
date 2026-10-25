use crate::model::{CapacitorModel, ResistorModel, TwoTerminalModel};
use crate::{
    circuit::{Circuit, Element},
    error::{SimError, SimResult},
    ids::NodeId,
    linalg::Matrix,
};
/// Immutable numeric plan; runtime histories and native handles live elsewhere.
#[derive(Debug, Clone)]
pub struct CompiledCircuit {
    pub(crate) circuit: Circuit,
    pub(crate) conductance: Matrix,
    pub(crate) charge: Matrix,
    pub(crate) rhs: Vec<f64>,
    pub(crate) voltage_branches: Vec<String>,
}
impl CompiledCircuit {
    /// Immutable public view; compilation exclusively owns numeric layout invariants.
    pub fn circuit(&self) -> &Circuit {
        &self.circuit
    }
    pub fn unknown_count(&self) -> usize {
        self.conductance.n
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
/// Branch unknowns follow the non-reference node voltages.
pub fn compile(circuit: &Circuit) -> SimResult<CompiledCircuit> {
    circuit.validate()?;
    let branches: Vec<String> = circuit
        .elements
        .iter()
        .filter_map(|e| match e {
            Element::Voltage { id, .. } => Some(id.clone()),
            _ => None,
        })
        .collect();
    let dimension = circuit.nodes.len() - 1 + branches.len();
    let mut a = Matrix::zeros(dimension)?;
    let mut rhs = vec![0.0; dimension];
    let mut branch = circuit.nodes.len() - 1;
    let mut charge = Matrix::zeros(dimension)?;
    for e in &circuit.elements {
        match e {
            Element::Capacitor { p, n, farads, .. } => stamp_pair(
                &mut charge,
                *p,
                *n,
                CapacitorModel(*farads).coefficients()?.capacitance,
            ),
            Element::Current { p, n, amps, .. } => {
                if let Some(i) = p.unknown() {
                    rhs[i] -= amps;
                }
                if let Some(i) = n.unknown() {
                    rhs[i] += amps;
                }
            }
            Element::Resistor { p, n, ohms, .. } => stamp_pair(
                &mut a,
                *p,
                *n,
                ResistorModel(*ohms).coefficients()?.conductance,
            ),
            Element::Voltage { p, n, volts, .. } => {
                for (node, sign) in [(*p, 1.0), (*n, -1.0)] {
                    if let Some(i) = node.unknown() {
                        a.add(i, branch, sign);
                        a.add(branch, i, sign);
                    }
                }
                rhs[branch] = *volts;
                branch += 1;
            }
        }
    }
    if a.values.iter().any(|v| !v.is_finite()) {
        return Err(SimError::invalid("component values overflowed assembly"));
    }
    Ok(CompiledCircuit {
        circuit: circuit.clone(),
        conductance: a,
        charge,
        rhs,
        voltage_branches: branches,
    })
}
