use crate::error::{SimError,SimResult};
/// Validate scalars before numeric allocation or division.
pub fn positive(value:f64,name:&str)->SimResult<f64>{
    if value.is_finite()&&value>0.0{Ok(value)}else{Err(SimError::invalid(format!("{name} must be finite and positive")))}
}
pub fn finite(value:f64,name:&str)->SimResult<f64>{
    if value.is_finite(){Ok(value)}else{Err(SimError::invalid(format!("{name} must be finite")))}
}

