use crate::{
    compile::CompiledCircuit,
    error::{SimError, SimResult},
    quantity::positive,
};
use serde::{Deserialize, Serialize};
/// Local step-doubling tolerances, not a global waveform accuracy guarantee.
#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(deny_unknown_fields)]
pub struct AdaptiveOptions {
    pub relative: f64,
    pub voltage_absolute: f64,
    pub current_absolute: f64,
    pub min_step_s: f64,
    pub max_attempts: usize,
}
impl AdaptiveOptions {
    pub fn validate(&self, max_step: f64) -> SimResult<()> {
        positive(self.relative, "relative tolerance")?;
        positive(self.voltage_absolute, "voltage_absolute")?;
        positive(self.current_absolute, "current_absolute")?;
        positive(self.min_step_s, "min_step_s")?;
        if self.relative >= 1.
            || self.min_step_s > max_step
            || self.max_attempts == 0
            || self.max_attempts > 1_000_000
        {
            return Err(SimError::invalid("invalid adaptive bounds"));
        }
        Ok(())
    }
    pub(crate) fn error(
        &self,
        plan: &CompiledCircuit,
        coarse: &[f64],
        fine: &[f64],
        order: i32,
    ) -> SimResult<f64> {
        let divisor = 2_f64.powi(order) - 1.;
        let mut norm = 0_f64;
        for (i, (&a, &b)) in coarse.iter().zip(fine).enumerate() {
            let absolute = if i < plan.circuit.nodes.len() - 1 {
                self.voltage_absolute
            } else {
                self.current_absolute
            };
            let scale = absolute + self.relative * a.abs().max(b.abs());
            let value = (a - b).abs() / divisor / scale;
            if !scale.is_finite() || !value.is_finite() {
                return Err(SimError::invalid("adaptive error overflow"));
            }
            norm = norm.max(value);
        }
        Ok(norm)
    }
}
