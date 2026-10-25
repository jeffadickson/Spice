use circuit_simulator::{backend::DenseLu,compile::compile,dc::solve_verified,error::{ErrorCode,SimResult},linalg::{LinearSolver,Matrix},measurement::value_at,netlist::parse,transient::{transient,Sample,TransientOptions}};
struct Liar;
impl LinearSolver for Liar{fn name(&self)->&'static str{"liar"}fn solve(&self,a:&Matrix,_:&[f64])->SimResult<Vec<f64>>{Ok(vec![0.0;a.n])}}
#[test]fn backend_success_cannot_hide_wrong_residual(){let a=Matrix{n:1,values:vec![1.0]};assert_eq!(solve_verified(&a,&[2.0],&Liar).unwrap_err().code,ErrorCode::NumericalFailure);}
#[test]fn invalid_transient_options_rejected(){let p=compile(&parse("title\nV1 a 0 1\nR1 a 0 1k").unwrap()).unwrap();for step in [0.0,-1.0,f64::NAN,f64::INFINITY]{assert!(transient(&p,&DenseLu,&TransientOptions{stop_s:1.0,step_s:step,max_steps:5}).is_err());}}
#[test]fn malformed_measurement_axis_rejected(){let s=vec![Sample{time_s:2.0,values:vec![1.0]},Sample{time_s:1.0,values:vec![2.0]}];assert!(value_at(&s,0,1.5).is_err());assert!(value_at(&s,9,1.5).is_err());}

