use crate::error::{SimError, SimResult};
use sha2::{Digest, Sha256};
/// Hashes serialized typed input; backend and requested outputs are part of that request.
/// This is provenance, not a semantic-equivalence or cross-version cache key.
pub fn request_hash<T: serde::Serialize>(request: &T) -> SimResult<String> {
    let bytes = serde_json::to_vec(request).map_err(|e| SimError::invalid(e.to_string()))?;
    Ok(format!("{:x}", Sha256::digest(bytes)))
}

/// Hash a typed payload in a named domain. Domain separation prevents a sweep
/// recipe digest from being mistaken for an executable request digest.
pub fn typed_hash<T: serde::Serialize>(domain: &str, value: &T) -> SimResult<String> {
    if domain.is_empty() || domain.len() > 64 || !domain.bytes().all(|byte| byte.is_ascii_lowercase() || byte == b'-' || byte == b'_') {
        return Err(SimError::invalid("invalid provenance hash domain"));
    }
    let bytes = serde_json::to_vec(value).map_err(|e| SimError::invalid(e.to_string()))?;
    let mut hasher = Sha256::new();
    hasher.update(domain.as_bytes());
    hasher.update([0]);
    hasher.update(bytes);
    Ok(format!("{:x}", hasher.finalize()))
}
