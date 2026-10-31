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
/// The `a` scale means atto, not an ampere annotation on an input number.
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
    let scales = [
        ("meg", 1e6),
        ("t", 1e12),
        ("g", 1e9),
        ("k", 1e3),
        ("m", 1e-3),
        ("u", 1e-6),
        ("n", 1e-9),
        ("p", 1e-12),
        ("f", 1e-15),
        ("a", 1e-18),
    ];
    // First recognize a bare scale, so 1A is atto. Only if that fails, permit
    // one trailing A after a recognized scale: 2mA stays milli, not atto.
    for candidate in std::iter::once(normalized).chain(normalized.strip_suffix('a')) {
        for (suffix, scale) in scales {
            if let Some(number) = candidate.strip_suffix(suffix) {
                if let Ok(value) = number.parse::<f64>() {
                    return finite(value * scale, "scaled scalar");
                }
            }
        }
    }
    Err(SimError::invalid(format!("unsupported scalar {text}")))
}
