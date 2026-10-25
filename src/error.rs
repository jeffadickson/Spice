use serde::Serialize;
use std::fmt;
/// Stable machine error classes; messages add context and are not API identifiers.
#[derive(Debug, Clone, PartialEq, Eq, Serialize)]
#[serde(rename_all = "snake_case")]
pub enum ErrorCode {
    InvalidInput,
    Unsupported,
    SingularMatrix,
    NumericalFailure,
    ResourceExhausted,
    Cancelled,
    IoError,
}
#[derive(Debug, Clone, Serialize)]
pub struct SimError {
    pub code: ErrorCode,
    pub message: String,
}
impl SimError {
    pub fn new(code: ErrorCode, message: impl Into<String>) -> Self {
        Self {
            code,
            message: message.into(),
        }
    }
    pub fn invalid(message: impl Into<String>) -> Self {
        Self::new(ErrorCode::InvalidInput, message)
    }
}
impl fmt::Display for SimError {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        write!(f, "{:?}: {}", self.code, self.message)
    }
}
impl std::error::Error for SimError {}
pub type SimResult<T> = Result<T, SimError>;
