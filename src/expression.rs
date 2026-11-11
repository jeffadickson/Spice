use crate::error::{SimError, SimResult};
/// Bounded single-input scalar model expression with analytic first derivative.
/// The wire adapter preserves the original Rust tuple-variant API.
#[derive(Debug, Clone, serde::Serialize, serde::Deserialize)]
#[serde(from = "WireExpr", into = "WireExpr")]
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
    /// Check structure and finite constants without evaluating a domain-sensitive
    /// function at an invented input. Domain checks belong to actual model trials.
    pub fn validate(&self) -> SimResult<()> {
        fn visit(expr: &Expr, depth: usize, left: &mut usize) -> SimResult<()> {
            if depth > 64 || *left == 0 { return Err(SimError::invalid("expression depth/node budget exceeded")); }
            *left -= 1;
            match expr {
                Expr::Constant(v) if !v.is_finite() => Err(SimError::invalid("nonfinite expression constant")),
                Expr::Add(a,b) | Expr::Multiply(a,b) | Expr::Divide(a,b) => { visit(a,depth+1,left)?; visit(b,depth+1,left) },
                Expr::Exp(a) | Expr::Log(a) => visit(a,depth+1,left),
                _ => Ok(())
            }
        }
        visit(self,0,&mut 1024)
    }
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

/// Explicit tagged wire fields keep .NET requests typed and reject unknown keys.
#[derive(serde::Serialize, serde::Deserialize)]
#[serde(tag="op",rename_all="snake_case",deny_unknown_fields)]
enum WireExpr {
    Constant { value: f64 }, Input,
    Add { left: Box<WireExpr>, right: Box<WireExpr> },
    Multiply { left: Box<WireExpr>, right: Box<WireExpr> },
    Divide { left: Box<WireExpr>, right: Box<WireExpr> },
    Exp { argument: Box<WireExpr> }, Log { argument: Box<WireExpr> },
}
impl From<WireExpr> for Expr {
    fn from(value: WireExpr) -> Self {
        fn b(v:Box<WireExpr>)->Box<Expr>{Box::new((*v).into())}
        match value {
            WireExpr::Constant{value}=>Self::Constant(value), WireExpr::Input=>Self::Input,
            WireExpr::Add{left,right}=>Self::Add(b(left),b(right)),
            WireExpr::Multiply{left,right}=>Self::Multiply(b(left),b(right)),
            WireExpr::Divide{left,right}=>Self::Divide(b(left),b(right)),
            WireExpr::Exp{argument}=>Self::Exp(b(argument)), WireExpr::Log{argument}=>Self::Log(b(argument)),
        }
    }
}
impl From<Expr> for WireExpr {
    fn from(value: Expr) -> Self {
        fn b(v:Box<Expr>)->Box<WireExpr>{Box::new((*v).into())}
        match value {
            Expr::Constant(value)=>Self::Constant{value}, Expr::Input=>Self::Input,
            Expr::Add(left,right)=>Self::Add{left:b(left),right:b(right)},
            Expr::Multiply(left,right)=>Self::Multiply{left:b(left),right:b(right)},
            Expr::Divide(left,right)=>Self::Divide{left:b(left),right:b(right)},
            Expr::Exp(argument)=>Self::Exp{argument:b(argument)}, Expr::Log(argument)=>Self::Log{argument:b(argument)},
        }
    }
}
