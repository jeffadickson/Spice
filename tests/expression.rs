use circuit_simulator::expression::Expr;
#[test]
fn polynomial_derivative_is_analytic() {
    let e = Expr::Add(
        Box::new(Expr::Multiply(Box::new(Expr::Input), Box::new(Expr::Input))),
        Box::new(Expr::Constant(3.0)),
    );
    for x in [-10.0, 0.0, 0.5, 50.0] {
        let (v, d) = e.evaluate(x).unwrap();
        assert_eq!(v, x * x + 3.0);
        assert_eq!(d, 2.0 * x);
    }
}
#[test]
fn expression_budget_and_nonfinite_are_rejected() {
    assert!(Expr::Constant(f64::NAN).evaluate(1.0).is_err());
    let mut e = Expr::Input;
    for _ in 0..70 {
        e = Expr::Add(Box::new(e), Box::new(Expr::Constant(1.0)));
    }
    assert!(e.evaluate(1.0).is_err());
}
