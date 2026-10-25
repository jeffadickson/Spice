//! Immutable, validated scalar component variations for reproducible experiments.
//!
//! Variations deliberately operate on the public circuit representation. They do
//! not mutate a compiled plan, so a caller cannot accidentally reuse a numeric
//! factorization after changing a circuit value.

use crate::{
    circuit::{Circuit, Element},
    error::{SimError, SimResult},
};

/// Physical meaning of a replaceable scalar. It is retained in reports so a
/// consumer does not need to infer units from an element-ID naming convention.
#[derive(Debug, Clone, Copy, PartialEq, Eq, serde::Serialize)]
#[serde(rename_all = "snake_case")]
pub enum ScalarKind { Resistance, Capacitance, Inductance, Voltage, Current }

impl ScalarKind {
    /// Canonical SI unit associated with this component scalar.
    pub const fn unit(self) -> &'static str {
        match self {
            Self::Resistance => "ohm",
            Self::Capacitance => "F",
            Self::Inductance => "H",
            Self::Voltage => "V",
            Self::Current => "A",
        }
    }
}

/// Look up the public scalar category for an element identity.
pub fn scalar_kind(circuit: &Circuit, element_id: &str) -> SimResult<ScalarKind> {
    let element = circuit.elements.iter().find(|element| element.id() == element_id)
        .ok_or_else(|| SimError::invalid("variation element ID is unknown"))?;
    Ok(match element {
        Element::Resistor { .. } => ScalarKind::Resistance,
        Element::Capacitor { .. } => ScalarKind::Capacitance,
        Element::Inductor { .. } => ScalarKind::Inductance,
        Element::Voltage { .. } => ScalarKind::Voltage,
        Element::Current { .. } => ScalarKind::Current,
    })
}

/// Return a validated circuit with exactly one scalar component value replaced.
///
/// The component ID is case-sensitive because IDs are stable API identities.
/// Resistance, capacitance, and inductance remain strictly positive; source
/// values may be signed. Unknown IDs and non-scalar names are rejected.
pub fn replace_scalar(circuit: &Circuit, element_id: &str, value: f64) -> SimResult<Circuit> {
    if element_id.is_empty() || element_id.len() > 256 || !value.is_finite() {
        return Err(SimError::invalid("invalid variation target or value"));
    }
    let mut varied = circuit.clone();
    let element = varied
        .elements
        .iter_mut()
        .find(|element| element.id() == element_id)
        .ok_or_else(|| SimError::invalid("variation element ID is unknown"))?;
    match element {
        Element::Resistor { ohms, .. } => *ohms = value,
        Element::Capacitor { farads, .. } => *farads = value,
        Element::Inductor { henries, .. } => *henries = value,
        Element::Voltage { volts, .. } => *volts = value,
        Element::Current { amps, .. } => *amps = value,
    }
    varied.validate()?;
    Ok(varied)
}

/// A finite ordered set of values for one component identity.
#[derive(Debug, Clone, serde::Serialize, serde::Deserialize)]
#[serde(deny_unknown_fields)]
pub struct ScalarSweep {
    pub element_id: String,
    pub values: Vec<f64>,
}

/// One fully described selected value, suitable for result manifests.
#[derive(Debug, Clone, PartialEq, serde::Serialize)]
pub struct ScalarSelection {
    pub element_id: String,
    pub kind: ScalarKind,
    pub unit: &'static str,
    pub value: f64,
}

impl ScalarSweep {
    /// Describe a value using the base circuit's explicit scalar kind and unit.
    pub fn selection(&self, circuit: &Circuit, value: f64) -> SimResult<ScalarSelection> {
        self.validate()?;
        if !self.values.contains(&value) {
            return Err(SimError::invalid("selection value is not declared by sweep"));
        }
        let kind = scalar_kind(circuit, &self.element_id)?;
        Ok(ScalarSelection { element_id: self.element_id.clone(), kind, unit: kind.unit(), value })
    }
}

impl ScalarSweep {
    /// Validate bounds before any derived circuit is allocated or executed.
    pub fn validate(&self) -> SimResult<()> {
        if self.element_id.is_empty()
            || self.element_id.len() > 256
            || self.values.is_empty()
            || self.values.len() > 64
            || self.values.iter().any(|value| !value.is_finite())
        {
            return Err(SimError::invalid("invalid scalar sweep"));
        }
        Ok(())
    }

    /// Materialize variants in caller-declared order, retaining duplicate values.
    pub fn circuits(&self, circuit: &Circuit) -> SimResult<Vec<Circuit>> {
        self.validate()?;
        self.values
            .iter()
            .map(|value| replace_scalar(circuit, &self.element_id, *value))
            .collect()
    }
}

/// A bounded Cartesian product of scalar sweeps. Dimension order is significant:
/// the first dimension changes slowest, which makes manifests reproducible.
#[derive(Debug, Clone, serde::Serialize, serde::Deserialize)]
#[serde(deny_unknown_fields)]
pub struct ScalarGrid {
    pub dimensions: Vec<ScalarSweep>,
}

impl ScalarGrid {
    /// Validate unique element targets and a maximum of 64 generated variants.
    pub fn validate(&self) -> SimResult<()> {
        if self.dimensions.is_empty() || self.dimensions.len() > 4 {
            return Err(SimError::invalid("scalar grid requires 1..4 dimensions"));
        }
        let mut names = std::collections::BTreeSet::new();
        let mut product = 1usize;
        for dimension in &self.dimensions {
            dimension.validate()?;
            if !names.insert(&dimension.element_id) {
                return Err(SimError::invalid("scalar grid targets must be unique"));
            }
            product = product
                .checked_mul(dimension.values.len())
                .ok_or_else(|| SimError::invalid("scalar grid size overflow"))?;
            if product > 64 {
                return Err(SimError::invalid("scalar grid exceeds 64 variants"));
            }
        }
        Ok(())
    }

    /// Return each complete immutable circuit and the matching dimension values.
    pub fn circuits(&self, circuit: &Circuit) -> SimResult<Vec<(Vec<f64>, Circuit)>> {
        self.validate()?;
        let mut result = vec![(Vec::new(), circuit.clone())];
        for dimension in &self.dimensions {
            let mut next = Vec::new();
            for (selected, base) in result {
                for value in &dimension.values {
                    let mut values = selected.clone();
                    values.push(*value);
                    next.push((
                        values,
                        replace_scalar(&base, &dimension.element_id, *value)?,
                    ));
                }
            }
            result = next;
        }
        Ok(result)
    }
}
