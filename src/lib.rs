//! Bounded linear-circuit reference engine. Sparse/GPU/nonlinear qualification is not claimed.
pub mod error;
pub mod ids;
pub mod circuit;
pub mod quantity;
pub mod linalg;
pub mod backend;
pub mod compile;
pub mod dc;
pub mod transient;
pub mod api;
pub mod ac;
pub mod grid;
pub mod measurement;
pub mod assertion;
pub mod netlist;
pub mod provenance;

