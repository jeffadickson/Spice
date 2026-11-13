//! Memoryless voltage-controlled resistance with exact first derivatives.
//! Control draws no current. This law adds no hidden charge or conductance floor.
use crate::{
    error::{SimError, SimResult},
    expression::Expr,
};

/// P-to-N output current and partial derivatives with respect to the output
/// and control differential voltages, in amperes and siemens respectively.
#[derive(Debug, Clone, Copy)]
pub struct ResistanceContribution {
    pub current: f64,
    pub output_siemens: f64,
    pub control_siemens: f64,
}

/// Evaluate I=Vout/R(Vcontrol). A zero/domain-invalid resistance is an error,
/// never an invented clamp. Finite negative resistance is an explicit active law.
pub fn linearize(
    expression: &Expr,
    output_v: f64,
    control_v: f64,
) -> SimResult<ResistanceContribution> {
    if !output_v.is_finite() {
        return Err(SimError::invalid("nonfinite behavioral resistor output"));
    }
    let (resistance, slope) = expression.evaluate(control_v)?;
    if resistance == 0. {
        return Err(SimError::invalid("zero behavioral resistance"));
    }
    let output_siemens = 1. / resistance;
    let current = output_v / resistance;
    // Divide before multiplying to avoid squaring a large resistance.
    let control_siemens = -current * (slope / resistance);
    if [current, output_siemens, control_siemens]
        .iter()
        .any(|v| !v.is_finite())
    {
        return Err(SimError::invalid(
            "behavioral resistor contribution overflow",
        ));
    }
    Ok(ResistanceContribution {
        current,
        output_siemens,
        control_siemens,
    })
}
