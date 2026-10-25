use crate::error::{SimError, SimResult};
use sha2::{Digest, Sha256};
/// Hashes serialized typed input; backend and requested outputs are part of that request.
/// This is provenance, not a semantic-equivalence or cross-version cache key.
pub fn request_hash<T: serde::Serialize>(request: &T) -> SimResult<String> {
    let bytes = serde_json::to_vec(request).map_err(|e| SimError::invalid(e.to_string()))?;
    Ok(format!("{:x}", Sha256::digest(bytes)))
}
