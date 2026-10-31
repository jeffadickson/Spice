//! Bounded passive model lowering; unsupported physics is never discarded.
use crate::{
    error::{ErrorCode, SimError, SimResult},
    quantity::spice_scalar,
};
use std::collections::{BTreeMap, BTreeSet};

/// Resolve forward-referenced constant R models before the primitive parser.
/// Only a positive default resistance is qualified. Geometry, temperature,
/// multiplicity, noise and per-instance overrides require separate contracts.
pub(crate) fn lower(lines: Vec<String>) -> SimResult<Vec<String>> {
    let mut names = BTreeSet::new();
    let mut models = BTreeMap::new();
    let mut retained = Vec::new();
    for line in lines {
        let tokens: Vec<_> = line.split_whitespace().collect();
        if !tokens
            .first()
            .is_some_and(|head| head.eq_ignore_ascii_case(".model"))
        {
            retained.push(line);
            continue;
        }
        if tokens.len() < 3 {
            return Err(SimError::invalid("incomplete model card"));
        }
        let name = tokens[1].to_ascii_uppercase();
        if !names.insert(name.clone()) {
            return Err(SimError::invalid(
                "duplicate model identifier across families",
            ));
        }
        let body = tokens[2..].join(" ");
        let kind = body.split(['(', ' ', '\t']).next().unwrap_or_default();
        if !kind.eq_ignore_ascii_case("r") {
            retained.push(line);
            continue;
        }
        let tail = body[kind.len()..].trim();
        let parameters = if tail.starts_with('(') {
            tail.strip_prefix('(')
                .and_then(|s| s.strip_suffix(')'))
                .ok_or_else(|| SimError::invalid("unbalanced resistor model"))?
        } else {
            tail
        };
        let separated = parameters.replace('=', " = ");
        let fields: Vec<_> = separated.split_whitespace().collect();
        if fields.len() != 3 || !fields[0].eq_ignore_ascii_case("r") || fields[1] != "=" {
            return Err(SimError::new(
                ErrorCode::Unsupported,
                "bounded R model requires only R=positive-resistance; geometry/temperature/noise are unsupported",
            ));
        }
        let value = spice_scalar(fields[2])?;
        if value <= 0. {
            return Err(SimError::invalid("model resistance must be positive"));
        }
        models.insert(name, value);
    }
    for line in &mut retained {
        let tokens: Vec<_> = line.split_whitespace().collect();
        if tokens.len() == 4 && tokens[0].starts_with(['R', 'r']) {
            if let Some(value) = models.get(&tokens[3].to_ascii_uppercase()) {
                *line = format!("{} {} {} {value}", tokens[0], tokens[1], tokens[2]);
            }
        }
    }
    Ok(retained)
}
