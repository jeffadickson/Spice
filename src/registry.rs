use crate::{
    backend::{DenseLu, DenseQr},
    error::{SimError, SimResult},
    linalg::LinearSolver,
};
use std::{collections::BTreeMap, sync::Arc};
/// Per-engine registry; never a mutable process-global singleton.
#[derive(Default)]
pub struct BackendRegistry {
    providers: BTreeMap<String, Arc<dyn LinearSolver>>,
}
impl BackendRegistry {
    pub fn reference() -> Self {
        let mut r = Self::default();
        r.register("lu", Arc::new(DenseLu)).unwrap();
        r.register("qr", Arc::new(DenseQr)).unwrap();
        r.register("faer", Arc::new(crate::backend_faer::FaerSparseLu)).unwrap();
        r
    }
    pub fn register(&mut self, id: &str, provider: Arc<dyn LinearSolver>) -> SimResult<()> {
        if id.is_empty() || self.providers.contains_key(id) {
            return Err(SimError::invalid("empty or duplicate backend ID"));
        }
        self.providers.insert(id.into(), provider);
        Ok(())
    }
    pub fn get(&self, id: &str) -> SimResult<Arc<dyn LinearSolver>> {
        self.providers.get(id).cloned().ok_or_else(|| {
            SimError::new(
                crate::error::ErrorCode::Unsupported,
                format!("unknown backend {id}"),
            )
        })
    }
    /// Sorted discovery is stable; opaque vendor handles never escape.
    pub fn capabilities(&self)->Vec<(String,crate::linalg::BackendCapabilities)>{
        self.providers.iter().map(|(id,p)|(id.clone(),p.capabilities())).collect()
    }
}
