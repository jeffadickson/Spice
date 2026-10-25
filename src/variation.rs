//! Immutable, validated scalar component variations for reproducible experiments.
//!
//! Variations deliberately operate on the public circuit representation. They do
//! not mutate a compiled plan, so a caller cannot accidentally reuse a numeric
//! factorization after changing a circuit value.

use crate::{
    circuit::{Circuit, Element},
    error::{SimError, SimResult},
};

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
