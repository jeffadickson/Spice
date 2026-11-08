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

/// The isolated FD host owns the exact pinned model and its 10 µm × 0.25 µm geometry.
#[derive(Debug, Clone, Copy)]
pub(crate) enum NativeModel {
    Bulk(Model),
    SoiFd,
}

impl NativeModel {
    pub(crate) fn evaluate(self, bias: [f64; 4]) -> SimResult<TerminalValues> {
        match self {
            Self::Bulk(model) => {
                let output = model.evaluate(bias)?;
                Ok(TerminalValues {
                    current: std::array::from_fn(|i| output[i]),
                    charge: Some(std::array::from_fn(|i| output[i + 4])),
                })
            }
            Self::SoiFd => {
                if bias.iter().any(|v| !v.is_finite() || v.abs() > 6.0) {
                    return Err(SimError::invalid(
                        "BSIM3 SOI FD bias outside static trial domain",
                    ));
                }
                let current = spice_bsim330_host::evaluate_fd(bias)
                    .map_err(|reason| SimError::new(ErrorCode::Unsupported, reason))?;
                Ok(TerminalValues {
                    current,
                    charge: None,
                })
            }
        }
    }
}

pub(crate) fn voltages(nodes: [NodeId; 4], x: &[f64]) -> [f64; 4] {
    nodes.map(|node| node.unknown().map_or(0.0, |i| x[i]))
}

/// Native terminal values in wire order. Static providers have no charge output.
pub(crate) struct TerminalValues {
    pub(crate) current: [f64; 4],
    pub(crate) charge: Option<[f64; 4]>,
}

impl TerminalValues {
    pub(crate) fn require_charge(&self) -> SimResult<[f64; 4]> {
        self.charge.ok_or_else(|| {
            SimError::new(ErrorCode::Unsupported, "native device charge unavailable")
        })
    }
}

/// Signed terminal values and their available bias derivatives.
pub(crate) struct Linearization {
    pub(crate) output: TerminalValues,
    pub(crate) derivative: [[f64; 4]; 4],
    pub(crate) charge_derivative: Option<[[f64; 4]; 4]>,
}

/// Centered differences share the same native evaluations for current and charge.
pub(crate) fn linearize(model: NativeModel, bias: [f64; 4]) -> SimResult<Linearization> {
    let output = model.evaluate(bias)?;
    let mut jacobian = [[0.0; 4]; 4];
    let mut charge_derivative = output.charge.map(|_| [[0.0; 4]; 4]);
    for col in 0..4 {
        let step = 1e-6 * bias[col].abs().max(1.0);
        let mut high = bias;
        let mut low = bias;
        high[col] += step;
        low[col] -= step;
        let high = model.evaluate(high)?;
        let low = model.evaluate(low)?;
        for row in 0..4 {
            jacobian[row][col] = (high.current[row] - low.current[row]) / (2.0 * step);
        }
        if let Some(derivative) = &mut charge_derivative {
            let (high, low) = (high.require_charge()?, low.require_charge()?);
            for row in 0..4 {
                derivative[row][col] = (high[row] - low[row]) / (2.0 * step);
            }
        }
    }
    if jacobian
        .iter()
        .chain(charge_derivative.iter().flatten())
        .flatten()
        .any(|v| !v.is_finite())
    {
        return Err(SimError::invalid("nonfinite BSIM3 derivative"));
    }
    Ok(Linearization {
        output,
        derivative: jacobian,
        charge_derivative,
    })
}
