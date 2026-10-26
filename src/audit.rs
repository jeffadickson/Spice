//! Independent physical checks over circuit elements and returned MNA states.
//! No audit reads assembled matrices or calls a numerical solver.
use crate::error::{SimError, SimResult};
use serde::Serialize;

/// Explicit check outcome; unavailable evidence is never presented as a pass.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize)]
#[serde(rename_all = "snake_case")]
pub enum CheckStatus {
    Passed,
    Failed,
    NotChecked,
}

/// A discrepancy in SI units and its effective absolute-plus-relative bound.
#[derive(Debug, Clone, Serialize)]
pub struct Check {
    pub id: String,
    pub status: CheckStatus,
    pub discrepancy: Option<f64>,
    pub tolerance: Option<f64>,
    pub unit: &'static str,
    pub reason: Option<String>,
}
impl Check {
    /// Overflow is a failed check with null metrics, preserving valid JSON.
    pub fn measured(
        id: impl Into<String>,
        discrepancy: f64,
        tolerance: f64,
        unit: &'static str,
    ) -> Self {
        let finite = discrepancy.is_finite() && tolerance.is_finite() && tolerance >= 0.;
        Self {
            id: id.into(),
            status: if finite && discrepancy.abs() <= tolerance {
                CheckStatus::Passed
            } else {
                CheckStatus::Failed
            },
            discrepancy: discrepancy.is_finite().then_some(discrepancy),
            tolerance: (tolerance.is_finite() && tolerance >= 0.).then_some(tolerance),
            unit,
            reason: (!finite).then(|| "nonfinite discrepancy or invalid bound".into()),
        }
    }
    pub fn not_checked(id: impl Into<String>, unit: &'static str, reason: &str) -> Self {
        Self {
            id: id.into(),
            status: CheckStatus::NotChecked,
            discrepancy: None,
            tolerance: None,
            unit,
            reason: Some(reason.into()),
        }
    }
}

/// Dimensional bounds; relative scaling uses sums of absolute physical terms.
#[derive(Debug, Clone, Copy)]
pub struct AuditOptions {
    pub current_absolute_a: f64,
    pub voltage_absolute_v: f64,
    pub power_absolute_w: f64,
    pub energy_absolute_j: f64,
    pub relative: f64,
}
impl Default for AuditOptions {
    fn default() -> Self {
        Self {
            current_absolute_a: 1e-10,
            voltage_absolute_v: 1e-8,
            power_absolute_w: 1e-10,
            energy_absolute_j: 1e-12,
            relative: 1e-8,
        }
    }
}
impl AuditOptions {
    pub fn validate(self) -> SimResult<()> {
        if [
            self.current_absolute_a,
            self.voltage_absolute_v,
            self.power_absolute_w,
            self.energy_absolute_j,
            self.relative,
        ]
        .iter()
        .any(|v| !v.is_finite() || *v <= 0.)
        {
            return Err(SimError::invalid("invalid audit tolerance"));
        }
        Ok(())
    }
}

/// Persistent evidence, including checks deliberately outside the supported profile.
#[derive(Debug, Clone, Serialize)]
pub struct AuditReport {
    pub checks: Vec<Check>,
}
impl AuditReport {
    pub fn has_failures(&self) -> bool {
        self.checks.iter().any(|c| c.status == CheckStatus::Failed)
    }
}
