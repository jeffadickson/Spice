use crate::{
    circuit::Element,
    compile::CompiledCircuit,
    error::{SimError, SimResult},
    waveform::Waveform,
};
use serde::{Deserialize, Serialize};
use std::collections::BTreeSet;
#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(deny_unknown_fields)]
pub struct SourceOverride {
    pub id: String,
    pub waveform: Waveform,
}
enum Target {
    Voltage {
        row: usize,
        base: f64,
    },
    Current {
        p: Option<usize>,
        n: Option<usize>,
        base: f64,
    },
}
struct BoundSource {
    target: Target,
    waveform: Waveform,
}
/// Sources own numeric descriptors only, never solver handles or mutable circuit state.
pub struct BoundSources {
    base: Vec<f64>,
    sources: Vec<BoundSource>,
    breakpoints: Vec<f64>,
}
impl BoundSources {
    pub fn new(plan: &CompiledCircuit, overrides: &[SourceOverride]) -> SimResult<Self> {
        if overrides.len() > 128 {
            return Err(SimError::invalid("source override budget exceeded"));
        }
        let mut ids = BTreeSet::new();
        let mut sources = Vec::new();
        let mut breakpoints = Vec::new();
        for source in overrides {
            if !ids.insert(&source.id) {
                return Err(SimError::invalid("duplicate source override"));
            }
            source.waveform.validate()?;
            breakpoints.extend(source.waveform.breakpoints());
            if breakpoints.len() > 100_000 {
                return Err(SimError::invalid("breakpoint budget exceeded"));
            }
            let element = plan
                .circuit
                .elements
                .iter()
                .find(|e| e.id() == source.id)
                .ok_or_else(|| SimError::invalid("unknown source override"))?;
            let target = match element {
                Element::Voltage { volts, .. } => Target::Voltage {
                    row: plan
                        .branch_index(&source.id)
                        .ok_or_else(|| SimError::invalid("missing source branch"))?,
                    base: *volts,
                },
                Element::Current { p, n, amps, .. } => Target::Current {
                    p: p.unknown(),
                    n: n.unknown(),
                    base: *amps,
                },
                _ => return Err(SimError::invalid("override must target independent source")),
            };
            sources.push(BoundSource {
                target,
                waveform: source.waveform.clone(),
            });
        }
        breakpoints.sort_by(f64::total_cmp);
        breakpoints.dedup();
        Ok(Self {
            base: plan.rhs.clone(),
            sources,
            breakpoints,
        })
    }
    pub fn rhs(&self, time: f64) -> SimResult<Vec<f64>> {
        let mut rhs = self.base.clone();
        for s in &self.sources {
            let v = s.waveform.at(time)?;
            match s.target {
                Target::Voltage { row, base } => rhs[row] += v - base,
                Target::Current { p, n, base } => {
                    if let Some(i) = p {
                        rhs[i] -= v - base;
                    }
                    if let Some(i) = n {
                        rhs[i] += v - base;
                    }
                }
            }
        }
        if rhs.iter().any(|v| !v.is_finite()) {
            return Err(SimError::invalid("source accumulation overflow"));
        }
        Ok(rhs)
    }
    pub fn next_breakpoint(&self, after: f64, until: f64) -> Option<f64> {
        self.breakpoints
            .get(self.breakpoints.partition_point(|t| *t <= after))
            .copied()
            .filter(|t| *t <= until)
    }
}
