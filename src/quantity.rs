use crate::error::{SimError, SimResult};
/// Validate scalars before numeric allocation or division.
pub fn positive(value: f64, name: &str) -> SimResult<f64> {
    if value.is_finite() && value > 0.0 {
        Ok(value)
    } else {
        Err(SimError::invalid(format!(
            "{name} must be finite and positive"
        )))
    }
}
pub fn finite(value: f64, name: &str) -> SimResult<f64> {
    if value.is_finite() {
        Ok(value)
    } else {
        Err(SimError::invalid(format!("{name} must be finite")))
    }
}

/// Small explicit SPICE scalar suffix profile, not a dimensional unit grammar.
/// Optional `s` and `Hz` decorations are case-insensitive and do not change
/// the scale: `MS` remains milliseconds, while `MEGHz` uses mega.
pub fn spice_scalar(text: &str) -> SimResult<f64> {
    let normalized = text.trim().to_ascii_lowercase();
    // Permit only these named decorations, not arbitrary ngspice trailing
    // letters or expressions. Rust's number parser still validates the mantissa.
    let normalized = normalized
        .strip_suffix("hz")
        .or_else(|| normalized.strip_suffix('s'))
        .unwrap_or(&normalized);
    if let Ok(value) = normalized.parse::<f64>() {
        return finite(value, "scalar");
    }
    for (suffix, scale) in [
        ("meg", 1e6),
        ("t", 1e12),
        ("g", 1e9),
        ("k", 1e3),
        ("m", 1e-3),
        ("u", 1e-6),
        ("n", 1e-9),
        ("p", 1e-12),
        ("f", 1e-15),
    ] {
        if let Some(number) = normalized.strip_suffix(suffix) {
            if let Ok(value) = number.parse::<f64>() {
                return finite(value * scale, "scaled scalar");
            }
        }
    }
    Err(SimError::invalid(format!("unsupported scalar {text}")))
}
