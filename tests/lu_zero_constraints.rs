//! Reproducer from a 1-ps MOS-memory first-ramp Newton correction. It is a
//! bounded assembled-system fixture, not an altered deck counted as corpus parity.
use circuit_simulator::{backend::DenseLu,backend_faer::FaerSparseLu,linalg::{LinearSolver,Matrix}};
#[test]
fn zero_source_constraint_chains_preserve_strict_backward_error_at_a_small_step(){
 let data:serde_json::Value=serde_json::from_str(include_str!("fixtures/mosmem_first_ramp_lu.json")).unwrap();
 let a=Matrix{n:data["n"].as_u64().unwrap() as usize,values:data["matrix"].as_array().unwrap().iter().map(|v|v.as_f64().unwrap()).collect()};
 let b:Vec<_>=data["rhs"].as_array().unwrap().iter().map(|v|v.as_f64().unwrap()).collect();
 let x=DenseLu.solve(&a,&b).unwrap();
 assert!(circuit_simulator::dc::verify_solution(&a,&b,&x).unwrap()<=1e-10);
 let reference=FaerSparseLu.solve(&a,&b).unwrap();
 for (value,expected) in x.iter().zip(reference){assert!((value-expected).abs()<=1e-12+1e-8*expected.abs());}
}
