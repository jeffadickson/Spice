//! Bound structural admission separately from real-domain evaluation.
//! Fixtures stay small even when a mutated traversal stops counting progress.
use circuit_simulator::{expression::Expr,error::ErrorCode};
use proptest::{prelude::*,test_runner::{Config,RngSeed,TestRunner}};

fn constant(value:f64)->Box<Expr> {Box::new(Expr::Constant(value))}
fn add_chain(depth:usize,right:bool)->Expr {
    let mut tree=Expr::Constant(0.);
    for _ in 0..depth {
        tree=if right {Expr::Add(constant(0.),Box::new(tree))}
             else {Expr::Add(Box::new(tree),constant(0.))};
    }
    tree
}
/// Full binary tree: height9 has1023 nodes and shallow recursion.
fn complete(height:usize)->Expr {
    if height==0 {Expr::Constant(0.)}
    else {Expr::Add(Box::new(complete(height-1)),Box::new(complete(height-1)))}
}

#[test]
fn structural_depth_is_inclusive_and_counts_both_binary_children() {
    for right in [false,true] {
        add_chain(64,right).validate().unwrap();
        assert_eq!(add_chain(65,right).validate().unwrap_err().code,ErrorCode::InvalidInput);
    }
}

#[test]
fn structural_node_budget_admits_1024_and_rejects_1025_nodes() {
    Expr::Abs(Box::new(complete(9))).validate().unwrap();
    let oversized=Expr::Add(constant(0.),Box::new(complete(9)));
    assert_eq!(oversized.validate().unwrap_err().code,ErrorCode::InvalidInput);
}

#[test]
fn structural_checks_visit_nested_constants_without_inventing_domain_inputs() {
    for value in [f64::NAN,f64::INFINITY,f64::NEG_INFINITY] {
        for tree in [Expr::Constant(value),Expr::Add(constant(value),constant(0.)),
            Expr::Multiply(constant(1.),constant(value)),Expr::Divide(constant(1.),constant(value))] {
            assert_eq!(tree.validate().unwrap_err().code,ErrorCode::InvalidInput);
        }
    }
    // Domain-sensitive evaluation is a different phase from structural checks.
    let domain=Expr::Log(constant(-1.));
    domain.validate().unwrap();
    assert_eq!(domain.evaluate(0.).unwrap_err().code,ErrorCode::InvalidInput);
}

#[test]
fn direct_evaluation_enforces_recursion_bounds_before_returning_finite_values() {
    // Public evaluate is callable without validate: each recursive path must
    // therefore retain its own bound. All mathematical values below are finite.
    assert_eq!(add_chain(64,true).evaluate(0.).unwrap(),(0.,0.));
    assert_eq!(add_chain(65,true).evaluate(0.).unwrap_err().code,ErrorCode::InvalidInput);
    let mut product=Expr::Constant(1.);
    for _ in 0..64 {product=Expr::Multiply(constant(1.),Box::new(product));}
    assert_eq!(product.evaluate(0.).unwrap(),(1.,0.));
    product=Expr::Multiply(constant(1.),Box::new(product));
    assert_eq!(product.evaluate(0.).unwrap_err().code,ErrorCode::InvalidInput);
    let mut sine=Expr::Constant(0.);
    for _ in 0..64 {sine=Expr::Sin(Box::new(sine));}
    assert_eq!(sine.evaluate(0.).unwrap(),(0.,0.));
    sine=Expr::Sin(Box::new(sine));
    assert_eq!(sine.evaluate(0.).unwrap_err().code,ErrorCode::InvalidInput);
    let mut identity=Expr::Constant(1.);
    // exp(log(1))=1 at every pair; no exploding exponential tower can mask
    // a missing depth increment with an unrelated overflow/domain failure.
    for _ in 0..32 {identity=Expr::Exp(Box::new(Expr::Log(Box::new(identity))));}
    assert_eq!(identity.evaluate(0.).unwrap(),(1.,0.));
    identity=Expr::Exp(Box::new(Expr::Log(Box::new(identity))));
    assert_eq!(identity.evaluate(0.).unwrap_err().code,ErrorCode::InvalidInput);
}

#[test]
fn linear_fractional_value_and_slope_match_the_analytic_reference() {
    // f(x)=(2x+1)/(3x+2); determinant2*2-3*1=1, hence f'=1/(3x+2)^2.
    let numerator=Expr::Add(Box::new(Expr::Multiply(constant(2.),Box::new(Expr::Input))),constant(1.));
    let denominator=Expr::Add(Box::new(Expr::Multiply(constant(3.),Box::new(Expr::Input))),constant(2.));
    let expression=Expr::Divide(Box::new(numerator),Box::new(denominator));
    expression.validate().unwrap();
    let config=Config{cases:64,rng_seed:RngSeed::Fixed(2026111927),failure_persistence:None,..Default::default()};
    TestRunner::new(config).run(&(-4_i32..65),|tick| {
        let x=tick as f64/16.;
        let (value,slope)=expression.evaluate(x).unwrap();
        let expected=(2.*x+1.)/(3.*x+2.);
        let derivative=1./(3.*x+2.).powi(2);
        prop_assert!((value-expected).abs()<1e-13,"fractional value mismatch");
        prop_assert!((slope-derivative).abs()<1e-13,"fractional derivative mismatch");
        Ok(())
    }).unwrap();
}
