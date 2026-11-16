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
    Abs(Box<Expr>), Asin(Box<Expr>), Acos(Box<Expr>),
    Sin(Box<Expr>), Cos(Box<Expr>), Tan(Box<Expr>), Atan(Box<Expr>), Sqrt(Box<Expr>), Floor(Box<Expr>), Ceil(Box<Expr>), Truncate(Box<Expr>),
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
                Expr::Exp(a) | Expr::Log(a) | Expr::Abs(a) | Expr::Asin(a) | Expr::Acos(a) | Expr::Sin(a) | Expr::Cos(a) | Expr::Tan(a) | Expr::Atan(a) | Expr::Sqrt(a) | Expr::Floor(a) | Expr::Ceil(a) | Expr::Truncate(a) => visit(a,depth+1,left),
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
            Self::Sin(a) | Self::Cos(a) | Self::Tan(a) | Self::Atan(a) |
            Self::Sqrt(a) | Self::Floor(a) | Self::Ceil(a) | Self::Truncate(a) => {
                let (x,dx)=a.inner(input,depth+1,budget)?;
                match self {
                    Self::Sin(_)=>(x.sin(),dx*x.cos()),
                    Self::Cos(_)=>(x.cos(),-dx*x.sin()),
                    Self::Tan(_)=>{let c=x.cos();(x.tan(),dx/c/c)},
                    // hypot avoids overflowing x*x for large finite inputs.
                    Self::Atan(_)=>{let h=x.hypot(1.);(x.atan(),dx/h/h)},
                    Self::Sqrt(_)=>{
                        if x<=0. {return Err(SimError::invalid("sqrt derivative domain"));}
                        // Twice sqrt(x) is finite and nonzero for every finite positive f64.
                        let root=x.sqrt();(root,dx/(2.*root))
                    },
                    // The selected slope is zero on each piece. At jumps these
                    // are an explicit SPICE piecewise convention, not a derivative proof.
                    Self::Floor(_)=>(x.floor(),0.), Self::Ceil(_)=>(x.ceil(),0.),
                    Self::Truncate(_)=>(x.trunc(),0.), _=>unreachable!()
                }
            }
            Self::Abs(a) => { let (x,dx)=a.inner(input,depth+1,budget)?; (x.abs(),if x==0.{0.}else{dx*x.signum()}) }
            Self::Asin(a) | Self::Acos(a) => {
                let (x,dx)=a.inner(input,depth+1,budget)?;
                // Endpoints have unbounded derivatives; do not invent a finite slope.
                if x.abs()>=1. {return Err(SimError::invalid("inverse trig derivative domain"));}
                let d=dx/((1.-x)*(1.+x)).sqrt();
                if matches!(self,Self::Asin(_)){(x.asin(),d)}else{(x.acos(),-d)}
            }            Self::Log(a) => {
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
    Sin { argument: Box<WireExpr> },
    Cos { argument: Box<WireExpr> },
    Tan { argument: Box<WireExpr> },
    Atan { argument: Box<WireExpr> },
    Sqrt { argument: Box<WireExpr> },
    Floor { argument: Box<WireExpr> },
    Ceil { argument: Box<WireExpr> },
    Truncate { argument: Box<WireExpr> },
    Abs { argument: Box<WireExpr> }, Asin { argument: Box<WireExpr> }, Acos { argument: Box<WireExpr> },
}
impl From<WireExpr> for Expr {
    fn from(value: WireExpr) -> Self {
        fn b(v:Box<WireExpr>)->Box<Expr>{Box::new((*v).into())}
        match value {
            WireExpr::Sin{argument}=>Self::Sin(b(argument)),
            WireExpr::Cos{argument}=>Self::Cos(b(argument)),
            WireExpr::Tan{argument}=>Self::Tan(b(argument)),
            WireExpr::Atan{argument}=>Self::Atan(b(argument)),
            WireExpr::Sqrt{argument}=>Self::Sqrt(b(argument)),
            WireExpr::Floor{argument}=>Self::Floor(b(argument)),
            WireExpr::Ceil{argument}=>Self::Ceil(b(argument)),
            WireExpr::Truncate{argument}=>Self::Truncate(b(argument)),
            WireExpr::Constant{value}=>Self::Constant(value), WireExpr::Input=>Self::Input,
            WireExpr::Add{left,right}=>Self::Add(b(left),b(right)),
            WireExpr::Multiply{left,right}=>Self::Multiply(b(left),b(right)),
            WireExpr::Divide{left,right}=>Self::Divide(b(left),b(right)),
            WireExpr::Abs{argument}=>Self::Abs(b(argument)), WireExpr::Asin{argument}=>Self::Asin(b(argument)), WireExpr::Acos{argument}=>Self::Acos(b(argument)),
            WireExpr::Exp{argument}=>Self::Exp(b(argument)), WireExpr::Log{argument}=>Self::Log(b(argument)),
        }
    }
}
impl From<Expr> for WireExpr {
    fn from(value: Expr) -> Self {
        fn b(v:Box<Expr>)->Box<WireExpr>{Box::new((*v).into())}
        match value {
            Expr::Sin(argument)=>Self::Sin{argument:b(argument)},
            Expr::Cos(argument)=>Self::Cos{argument:b(argument)},
            Expr::Tan(argument)=>Self::Tan{argument:b(argument)},
            Expr::Atan(argument)=>Self::Atan{argument:b(argument)},
            Expr::Sqrt(argument)=>Self::Sqrt{argument:b(argument)},
            Expr::Floor(argument)=>Self::Floor{argument:b(argument)},
            Expr::Ceil(argument)=>Self::Ceil{argument:b(argument)},
            Expr::Truncate(argument)=>Self::Truncate{argument:b(argument)},
            Expr::Constant(value)=>Self::Constant{value}, Expr::Input=>Self::Input,
            Expr::Add(left,right)=>Self::Add{left:b(left),right:b(right)},
            Expr::Multiply(left,right)=>Self::Multiply{left:b(left),right:b(right)},
            Expr::Divide(left,right)=>Self::Divide{left:b(left),right:b(right)},
            Expr::Abs(argument)=>Self::Abs{argument:b(argument)}, Expr::Asin(argument)=>Self::Asin{argument:b(argument)}, Expr::Acos(argument)=>Self::Acos{argument:b(argument)},
            Expr::Exp(argument)=>Self::Exp{argument:b(argument)}, Expr::Log(argument)=>Self::Log{argument:b(argument)},
        }
    }
}
