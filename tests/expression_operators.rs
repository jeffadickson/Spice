//! Scalar math laws: independent values, chain derivatives and malformed wire domains.
use circuit_simulator::expression::Expr;
fn unary(op:&str, argument:serde_json::Value)->Expr {
 serde_json::from_value(serde_json::json!({"op":op,"argument":argument})).unwrap()
}
fn input()->serde_json::Value {serde_json::json!({"op":"input"})}
#[test]
fn smooth_functions_match_values_and_chain_derivatives() {
 for (op,x,value,slope) in [
  ("sin",0.7,0.644217687237691,0.7648421872844885),
  ("cos",0.7,0.7648421872844885,-0.644217687237691),
  ("tan",0.7,0.8422883804630794,1.709449715863117),
  ("atan",2.0,1.1071487177940904,0.2),
  ("sqrt",4.0,2.0,0.25),
 ] {
  let e=unary(op,input());e.validate().unwrap();let (v,d)=e.evaluate(x).unwrap();
  assert!((v-value).abs()<1e-13,"{op} value {v}");assert!((d-slope).abs()<1e-13,"{op} derivative {d}");
  let chain=unary(op,serde_json::json!({"op":"multiply","left":{"op":"constant","value":2.0},"right":input()}));
  let (cv,cd)=chain.evaluate(x/2.).unwrap();assert!((cv-value).abs()<1e-13);assert!((cd-2.*slope).abs()<1e-13);
 }
}
#[test]
fn rounding_is_piecewise_constant_and_retains_signed_semantics() {
 for (op,x,expected) in [("floor",-1.2,-2.),("ceil",-1.2,-1.),("truncate",-1.2,-1.),("floor",1.2,1.),("ceil",1.2,2.),("truncate",1.2,1.)] {
  let (value,slope)=unary(op,input()).evaluate(x).unwrap();assert_eq!(value,expected);assert_eq!(slope,0.);
 }
}
#[test]
fn undefined_domains_and_unknown_wire_fields_do_not_invent_values() {
 let sqrt=unary("sqrt",input());for x in [-1.,0.] {assert!(sqrt.evaluate(x).is_err());}
 assert!(sqrt.evaluate(f64::NAN).is_err());
 assert!(serde_json::from_value::<Expr>(serde_json::json!({"op":"sin","argument":input(),"unknown":true})).is_err());
 let mut deep=input();for _ in 0..65 {deep=serde_json::json!({"op":"sin","argument":deep});}
 assert!(serde_json::from_value::<Expr>(deep).unwrap().validate().is_err());
}
proptest::proptest! {
 #![proptest_config(proptest::test_runner::Config::with_cases(64))]
 #[test]
 fn derivatives_and_wire_roundtrip_match_centered_differences(x in -1.0_f64..1.0) {
  for op in ["sin","cos","tan","atan"] {
   let e=unary(op,input());let step=1e-5;let (_,d)=e.evaluate(x).unwrap();
   let finite=(e.evaluate(x+step).unwrap().0-e.evaluate(x-step).unwrap().0)/(2.*step);
   proptest::prop_assert!((finite-d).abs()<1e-8);
   let decoded:Expr=serde_json::from_value(serde_json::to_value(&e).unwrap()).unwrap();
   proptest::prop_assert_eq!(decoded.evaluate(x).unwrap(),e.evaluate(x).unwrap());
  }
 }
}

#[test]
fn sqrt_chain_preserves_a_representable_subnormal_scaled_derivative() {
 let k=f64::from_bits(1);
 let e=unary("sqrt",serde_json::json!({"op":"multiply","left":{"op":"constant","value":k},"right":input()}));
 let (_,d)=e.evaluate(1.).unwrap();let expected=k.sqrt()/2.;
 assert!(d>0.);assert!((d/expected-1.).abs()<1e-14);
}

#[test]
fn sqrt_chain_preserves_a_finite_large_derivative_without_intermediate_overflow() {
 let e=unary("sqrt",serde_json::json!({"op":"add","left":{"op":"constant","value":1e-300},"right":{"op":"multiply","left":{"op":"constant","value":2e158},"right":input()}}));
 let (_,d)=e.evaluate(0.).unwrap();assert!((d/1e308-1.).abs()<1e-14);
}
