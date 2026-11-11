//! Public-wire DC behavior, independently known values and analytic Jacobian checks.
use circuit_simulator::api::{Request, run};

fn request(backend: &str, volts: f64) -> Request {
    serde_json::from_value(serde_json::json!({
        "schema_version":"1","case_id":"behavioral_log","backend":backend,
        "circuit":{"nodes":["0","in","out"],"elements":[
            {"kind":"voltage","id":"V1","p":1,"n":0,"volts":volts},
            {"kind":"behavioral_voltage","id":"B1","p":2,"n":0,"cp":1,"cn":0,
             "expression":{"op":"log","argument":{"op":"input"}}},
            {"kind":"resistor","id":"R1","p":2,"n":0,"ohms":1000.0}
        ]},"analysis":{"kind":"operating_point"}
    })).expect("bounded behavioral expression must deserialize")
}

#[test]
fn logarithmic_voltage_uses_the_real_solver_and_independent_audit() {
    for backend in ["lu","faer"] {
        let result=run(&request(backend,2.7)).unwrap();
        assert!((result.samples[0].values[1]-0.9932517730102834).abs()<1e-12);
        let branch=result.signals.iter().position(|s|s.id=="i(B1)").unwrap();
        assert!((result.samples[0].values[branch]+0.0009932517730102834).abs()<1e-14);
        assert!(result.consistency.checks.iter().any(|c|c.id=="constraint:B1"));
    }
}

#[test]
fn nonpositive_logarithm_domain_is_not_replaced_by_zero() {
    for volts in [0.,-2.7] { assert!(run(&request("lu",volts)).is_err()); }
}

#[test]
fn logarithmic_constitutive_jacobian_matches_centered_differences() {
    use circuit_simulator::{compile::compile,newton::linearize};
    let q=request("lu",2.7);
    let plan=compile(&q.circuit).unwrap();
    let state=vec![2.7,0.9932517730102834,0.,-0.0009932517730102834];
    let exact=linearize(&plan,&state).unwrap();
    assert!((exact.jacobian.values[3*exact.jacobian.n]+1./2.7).abs()<1e-14);
    for column in 0..state.len() {
        let mut plus=state.clone();let mut minus=state.clone();
        plus[column]+=1e-6;minus[column]-=1e-6;
        let p=linearize(&plan,&plus).unwrap();let m=linearize(&plan,&minus).unwrap();
        for row in 0..state.len() {
            let difference=(p.residual[row]-m.residual[row])/2e-6;
            assert!((exact.jacobian.values[row*exact.jacobian.n+column]-difference).abs()<1e-8);
        }
    }
}

#[test]
fn independent_constraint_audit_detects_corrupted_behavioral_output() {
    use circuit_simulator::audit::{dc,AuditOptions,CheckStatus};
    let q=request("lu",2.7);let mut result=run(&q).unwrap();
    result.samples[0].values[1]+=0.1;
    let audit=dc(&q.circuit,&result.samples[0].values,AuditOptions::default(),None).unwrap();
    assert!(audit.checks.iter().any(|check|check.id=="constraint:B1"&&check.status==CheckStatus::Failed));
}

#[test]
fn dynamic_analysis_is_explicitly_refused_until_qualified() {
    let mut wire=serde_json::to_value(request("lu",2.7)).unwrap();
    wire["analysis"]=serde_json::json!({"kind":"ac","frequencies_hz":[1.0],"source_phasors":[]});
    let q:Request=serde_json::from_value(wire).unwrap();
    assert_eq!(run(&q).unwrap_err().code,circuit_simulator::error::ErrorCode::Unsupported);
}

#[test]
fn unknown_expression_operations_and_fields_are_not_silently_ignored() {
    let mut wire=serde_json::to_value(request("lu",2.7)).unwrap();
    wire["circuit"]["elements"][1]["expression"]["op"]=serde_json::json!("shell");
    assert!(serde_json::from_value::<Request>(wire).is_err());
    let mut wire=serde_json::to_value(request("lu",2.7)).unwrap();
    wire["circuit"]["elements"][1]["expression"]["invented"]=serde_json::json!(42);
    assert!(serde_json::from_value::<Request>(wire).is_err());
}

proptest::proptest! {
    #![proptest_config(proptest::test_runner::Config {
        cases: 64,
        rng_seed: proptest::test_runner::RngSeed::Fixed(2026111101),
        ..Default::default()
    })]
    #[test]
    fn positive_logarithms_preserve_voltage_and_loaded_branch_current(volts in 0.05f64..20.0) {
        let result=run(&request("lu",volts)).unwrap();
        let expected=volts.ln();
        proptest::prop_assert!((result.samples[0].values[1]-expected).abs()<1e-10);
        let branch=result.signals.iter().position(|s|s.id=="i(B1)").unwrap();
        proptest::prop_assert!((result.samples[0].values[branch]+expected/1000.).abs()<1e-12);
        proptest::prop_assert!(result.consistency.checks.iter().any(|c|c.id=="constraint:B1"));
    }
}
