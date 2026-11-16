//! Original RCA3040 correction matrix: retain reverse-junction sensor current.
use circuit_simulator::{backend::DenseLu,linalg::{LinearSolver,Matrix}};
#[test]
fn rca3040_tiny_sensor_current_satisfies_every_original_row() {
    let f:serde_json::Value=serde_json::from_str(include_str!("fixtures/lu-rca3040-sensor-system.json")).unwrap();
    let n=f["n"].as_u64().unwrap() as usize;
    let mut values=vec![0.;n*n];
    for e in f["entries"].as_array().unwrap() {values[e[0].as_u64().unwrap() as usize*n+e[1].as_u64().unwrap() as usize]=e[2].as_f64().unwrap();}
    let rhs:Vec<_>=f["rhs"].as_array().unwrap().iter().map(|v|v.as_f64().unwrap()).collect();
    let matrix=Matrix{n,values};
    for prepared in [false,true] {
        let x=if prepared {DenseLu.prepare(&matrix).unwrap().unwrap().solve(&rhs).unwrap()}
              else {DenseLu.solve(&matrix,&rhs).unwrap()};
        // This current is ~1e-272 A; replacing the tiny conductance by zero
        // would violate its unchanged homogeneous row at relative error one.
        assert!(x[67]>0. && x[67]<1e-265);
        for row in 0..n {
            let mut sum=0.;let mut scale=rhs[row].abs();
            for col in 0..n {let p=matrix.values[row*n+col]*x[col];sum+=p;scale+=p.abs();}
            assert!((sum-rhs[row]).abs()<=1e-10*scale.max(f64::MIN_POSITIVE),"row {row}, prepared {prepared}");
        }
    }
}
