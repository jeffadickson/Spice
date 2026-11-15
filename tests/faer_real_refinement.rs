use circuit_simulator::{backend_faer::FaerSparseLu,linalg::{LinearSolver,Matrix},sparse::CscMatrix,solver_contract::verify_csc_solution};
#[test]fn prepared_coupled_transient_matrix_retains_strict_componentwise_gate(){
 let j:serde_json::Value=serde_json::from_str(include_str!("../compat/benchmarks/faer-coupled-residual.json")).unwrap();let n=j["n"].as_u64().unwrap()as usize;let mut a=Matrix::zeros(n).unwrap();
 for term in j["entries"].as_array().unwrap(){a.values[term[0].as_u64().unwrap()as usize*n+term[1].as_u64().unwrap()as usize]=term[2].as_f64().unwrap();}
 let a=CscMatrix::from_dense(&a).unwrap();let rhs:Vec<f64>=serde_json::from_value(j["rhs"].clone()).unwrap();let raw:Vec<f64>=serde_json::from_value(j["values"].clone()).unwrap();
 assert!(verify_csc_solution(&a,&rhs,&raw).is_err(),"Retained uncorrected counterexample must fail the unchanged gate");
 for x in [FaerSparseLu.prepare_csc(&a).unwrap().unwrap().solve(&rhs).unwrap()]{assert!(verify_csc_solution(&a,&rhs,&x).unwrap()<=1e-10);}
}
