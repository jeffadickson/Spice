use circuit_simulator::expression::Expr;
#[test] fn unary_values_and_derivatives_follow_independent_laws(){
 for x in [-0.9_f64,-0.25,0.25,0.9]{
  for name in ["abs","asin","acos"]{
   let e:Expr=serde_json::from_value(serde_json::json!({"op":name,"argument":{"op":"input"}})).unwrap();let(v,d)=e.evaluate(x).unwrap();
   let expected=match name{"abs"=>x.abs(),"asin"=>x.asin(),_=>x.acos()};assert!((v-expected).abs()<1e-14);
   let h=1e-6;let f=|x:f64|match name{"abs"=>x.abs(),"asin"=>x.asin(),_=>x.acos()};assert!((d-(f(x+h)-f(x-h))/(2.*h)).abs()<1e-8);
  }
 }
}
#[test] fn inverse_trig_endpoint_and_outside_domain_refuse_without_clamping(){
 for op in ["asin","acos"]{let e:Expr=serde_json::from_value(serde_json::json!({"op":op,"argument":{"op":"input"}})).unwrap();for x in [-2.,-1.,1.,2.]{assert!(e.evaluate(x).is_err());}}
}
