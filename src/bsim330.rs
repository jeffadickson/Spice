//! Bounded bulk BSIM3 3.3.0 native model contract.
use crate::{
    error::{ErrorCode, SimError, SimResult},
    ids::NodeId,
};
use serde::{Deserialize, Serialize};

/// SI geometry; NCH and NSUB use the model card convention of cm⁻³.
#[derive(Debug, Clone, Copy, Serialize, Deserialize)]
#[serde(deny_unknown_fields)]
pub struct Model {
    pub polarity: i8,
    pub tox: f64,
    pub nch: f64,
    pub nsub: f64,
    pub vth0: f64,
    pub w: f64,
    pub l: f64,
    pub pd: f64,
    pub ps: f64,
    pub ad: f64,
    #[serde(rename = "as")]
    pub source_area: f64,
}

impl Model {
    fn parameters(self) -> [f64; 11] {
        [
            f64::from(self.polarity),
            self.tox,
            self.nch,
            self.nsub,
            self.vth0,
            self.w,
            self.l,
            self.pd,
            self.ps,
            self.ad,
            self.source_area,
        ]
    }

    pub(crate) fn validate(self) -> SimResult<()> {
        if !matches!(self.polarity, -1 | 1)
            || self.parameters().iter().any(|v| !v.is_finite())
            || [self.tox, self.nch, self.nsub, self.w, self.l]
                .iter()
                .any(|v| *v <= 0.0)
            || [self.pd, self.ps, self.ad, self.source_area]
                .iter()
                .any(|v| *v < 0.0)
        {
            return Err(SimError::invalid("invalid BSIM3 3.3.0 model or geometry"));
        }
        Ok(())
    }

    pub(crate) fn evaluate(self, bias: [f64; 4]) -> SimResult<[f64; 8]> {
        if bias.iter().any(|v| !v.is_finite()) {
            return Err(SimError::invalid("nonfinite BSIM3 bias"));
        }
        spice_bsim330_host::evaluate(self.parameters(), bias)
            .map_err(|reason| SimError::new(ErrorCode::Unsupported, reason))
    }
}

pub(crate) fn voltages(nodes: [NodeId; 4], x: &[f64]) -> [f64; 4] {
    nodes.map(|node| node.unknown().map_or(0.0, |i| x[i]))
}

/// Signed terminal currents and charges, followed by their bias derivatives.
/// Rows 0–3 are current and rows 4–7 are charge, both in D/G/S/B order.
pub(crate) struct Linearization {
    pub(crate) output: [f64; 8],
    pub(crate) derivative: [[f64; 4]; 8],
}

/// Centered differences share the same native evaluations for current and charge.
pub(crate) fn linearize(model: Model, bias: [f64; 4]) -> SimResult<Linearization> {
    let output = model.evaluate(bias)?;
    let mut jacobian = [[0.0; 4]; 8];
    for col in 0..4 {
        let step = 1e-6 * bias[col].abs().max(1.0);
        let mut high = bias;
        let mut low = bias;
        high[col] += step;
        low[col] -= step;
        let high = model.evaluate(high)?;
        let low = model.evaluate(low)?;
        for row in 0..8 {
            jacobian[row][col] = (high[row] - low[row]) / (2.0 * step);
        }
    }
    if jacobian.iter().flatten().any(|v| !v.is_finite()) {
        return Err(SimError::invalid("nonfinite BSIM3 derivative"));
    }
    Ok(Linearization {
        output,
        derivative: jacobian,
    })
}
