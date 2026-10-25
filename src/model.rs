use crate::{error::SimResult, quantity::positive};
/// Minimal built-in linear-device contract; native plugin ABI and nonlinear states are deferred.
#[derive(Debug, Clone, Copy)]
pub struct LinearContribution {
    pub conductance: f64,
    pub capacitance: f64,
}
pub trait TwoTerminalModel {
    fn coefficients(&self) -> SimResult<LinearContribution>;
}
pub struct ResistorModel(pub f64);
pub struct CapacitorModel(pub f64);
impl TwoTerminalModel for ResistorModel {
    fn coefficients(&self) -> SimResult<LinearContribution> {
        let r = positive(self.0, "resistance")?;
        Ok(LinearContribution {
            conductance: 1.0 / r,
            capacitance: 0.0,
        })
    }
}
impl TwoTerminalModel for CapacitorModel {
    fn coefficients(&self) -> SimResult<LinearContribution> {
        let c = positive(self.0, "capacitance")?;
        Ok(LinearContribution {
            conductance: 0.0,
            capacitance: c,
        })
    }
}
