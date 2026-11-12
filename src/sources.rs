use crate::{
    circuit::Element,
    compile::CompiledCircuit,
    error::{SimError, SimResult},
    waveform::Waveform,
};
use serde::{Deserialize, Serialize};
use std::collections::{BTreeMap, BTreeSet};
#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(deny_unknown_fields)]
pub struct SourceOverride {
    pub id: String,
    pub waveform: Waveform,
}
enum Target {
    Voltage { row: usize },
    Current { p: Option<usize>, n: Option<usize> },
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
        let mut selected = BTreeMap::new();
        let mut breakpoints = Vec::new();
        for source in overrides {
            if !ids.insert(source.id.as_str()) {
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
                Element::Voltage { .. } => Target::Voltage {
                    row: plan
                        .branch_index(&source.id)
                        .ok_or_else(|| SimError::invalid("missing source branch"))?,
                },
                Element::Current { p, n, .. } => Target::Current {
                    p: p.unknown(),
                    n: n.unknown(),
                },
                _ => return Err(SimError::invalid("override must target independent source")),
            };
            selected.insert(
                source.id.as_str(),
                BoundSource {
                    target,
                    waveform: source.waveform.clone(),
                },
            );
        }
        breakpoints.sort_by(f64::total_cmp);
        breakpoints.dedup();
        // Preserve original current-source order, including nominal constants.
        // Grouping unbound currents before overrides can introduce an overflow
        // that the original finite signed accumulation did not have.
        let mut base = vec![0.; plan.unknown_count()];
        for element in &plan.circuit.elements {
            if let Some(source) = selected.remove(element.id()) {
                sources.push(source);
                continue;
            }
            match element {
                Element::Voltage { id, volts, .. } => {
                    let row = plan
                        .branch_index(id)
                        .ok_or_else(|| SimError::invalid("missing nominal source branch"))?;
                    base[row] = *volts;
                }
                Element::Current { p, n, amps, .. } => {
                    sources.push(BoundSource {
                        target: Target::Current {
                            p: p.unknown(),
                            n: n.unknown(),
                        },
                        waveform: Waveform::Constant { value: *amps },
                    });
                }
                _ => {}
            }
        }
        if base.iter().any(|value| !value.is_finite()) {
            return Err(SimError::invalid("unbound source accumulation overflow"));
        }
        Ok(Self {
            base,
            sources,
            breakpoints,
        })
    }
    pub fn rhs(&self, time: f64) -> SimResult<Vec<f64>> {
        if !time.is_finite() || time < 0. {
            return Err(SimError::invalid("invalid source time"));
        }
        let mut rhs = self.base.clone();
        for s in &self.sources {
            let v = s.waveform.at_validated(time)?;
            match s.target {
                Target::Voltage { row } => rhs[row] = v,
                Target::Current { p, n } => {
                    if let Some(i) = p {
                        rhs[i] -= v;
                    }
                    if let Some(i) = n {
                        rhs[i] += v;
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
        let finite = self.breakpoints
            .get(self.breakpoints.partition_point(|t| *t <= after))
            .copied()
            .filter(|t| *t <= until);
        self.sources.iter().filter_map(|s|s.waveform.next_periodic_breakpoint(after,until))
            .chain(finite).min_by(f64::total_cmp)
    }
}
