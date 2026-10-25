use crate::error::{SimError, SimResult};
/// Bounded host expression experiment; not an integrated nonlinear device or general interpreter.
#[derive(Debug, Clone)]
pub enum Expr {
    Constant(f64),
    Input,
    Add(Box<Expr>, Box<Expr>),
    Multiply(Box<Expr>, Box<Expr>),
    Divide(Box<Expr>, Box<Expr>),
    Exp(Box<Expr>),
    Log(Box<Expr>),
}
impl Expr {
    pub fn evaluate(&self, input: f64) -> SimResult<(f64, f64)> {
        if !input.is_finite() {
            return Err(SimError::invalid("nonfinite expression input"));
        }
        let mut budget = 1024;
        self.inner(input, 0, &mut budget)
    }
    fn inner(&self, input: f64, depth: usize, budget: &mut usize) -> SimResult<(f64, f64)> {
        if depth > 64 || *budget == 0 {
            return Err(SimError::invalid("expression depth/node budget exceeded"));
        }
        *budget -= 1;
        let (value, derivative) = match self {
            Self::Constant(v) => (*v, 0.),
            Self::Input => (input, 1.),
            Self::Add(a, b) => {
                let (x, dx) = a.inner(input, depth + 1, budget)?;
                let (y, dy) = b.inner(input, depth + 1, budget)?;
                (x + y, dx + dy)
            }
            Self::Multiply(a, b) => {
                let (x, dx) = a.inner(input, depth + 1, budget)?;
                let (y, dy) = b.inner(input, depth + 1, budget)?;
                (x * y, dx * y + x * dy)
            }
            Self::Divide(a, b) => {
                let (x, dx) = a.inner(input, depth + 1, budget)?;
                let (y, dy) = b.inner(input, depth + 1, budget)?;
                if y == 0. {
                    return Err(SimError::invalid("division by zero"));
                }
                (x / y, (dx - (x / y) * dy) / y)
            }
            Self::Exp(a) => {
                let (x, dx) = a.inner(input, depth + 1, budget)?;
                let value = x.exp();
                (value, value * dx)
            }
            Self::Log(a) => {
                let (x, dx) = a.inner(input, depth + 1, budget)?;
                if x <= 0. {
                    return Err(SimError::invalid("log domain"));
                }
                (x.ln(), dx / x)
            }
        };
        if !value.is_finite() || !derivative.is_finite() {
            return Err(SimError::invalid("expression overflow/domain error"));
        }
        Ok((value, derivative))
    }
}
