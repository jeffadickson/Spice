//! Regression derived from the original XSPICE voltage-gain oracle: all values
//! are SI, and independent source references remain fixed during the ramp.
use circuit_simulator::api::{Request, run};

const REQUEST: &str = r#"{"case_id":"differential-gain-ramp","circuit":{"nodes":["0","in","mid","out","ref","low"],"elements":[{"kind":"voltage","volts":0,"id":"drive","p":1,"n":0},{"kind":"voltage","volts":0.5,"id":"reference","p":4,"n":0},{"kind":"voltage","volts":0.2,"id":"low","p":5,"n":0},{"kind":"voltage","volts":1.1,"id":"offset","p":2,"n":5},{"kind":"vcvs","cp":1,"cn":4,"gain":-2,"id":"gain","p":3,"n":2},{"kind":"resistor","ohms":1000,"id":"load","p":3,"n":0}]},"analysis":{"kind":"transient_configured","options":{"stop_s":0.001,"max_step_s":0.0001,"max_steps":100,"method":"backward_euler","sources":[{"id":"drive","waveform":{"kind":"pwl","points":[{"time_s":0,"value":0},{"time_s":0.001,"value":1}]}}]}},"backend":"lu","schema_version":"1"}"#;

#[test]
fn differential_gain_ramp_preserves_nonzero_reference_constraints_on_both_providers() {
    for backend in ["lu", "faer"] {
        let mut request: Request = serde_json::from_str(REQUEST).unwrap();
        request.backend = backend.into();
        let result = run(&request).unwrap_or_else(|error| panic!("{backend}: {error:?}"));
        let output = result
            .signals
            .iter()
            .position(|s| s.id == "v(out)")
            .unwrap();
        let reference = result
            .signals
            .iter()
            .position(|s| s.id == "v(ref)")
            .unwrap();
        let low = result
            .signals
            .iter()
            .position(|s| s.id == "v(low)")
            .unwrap();
        assert_eq!(result.samples.len(), 11);
        for sample in result.samples {
            // Vout=.2+1.1-2*(1000*t-.5)=2.3-2000*t V.
            assert!((sample.values[output] - (2.3 - 2000. * sample.time_s)).abs() < 1e-12);
            assert!((sample.values[reference] - 0.5).abs() < 1e-14);
            assert!((sample.values[low] - 0.2).abs() < 1e-14);
        }
    }
}

#[test]
fn changing_only_gain_drive_rhs_meets_componentwise_residual_policy() {
    use circuit_simulator::{
        backend::DenseLu, backend_faer::FaerSparseLu, compile::compile, error::ErrorCode,
        linalg::LinearSolver, solver_contract::verify_csc_solution,
    };
    let request: Request = serde_json::from_str(REQUEST).unwrap();
    let plan = compile(&request.circuit).unwrap();
    let matrix = plan.conductance_sparse();
    let dense = plan.conductance_reference();
    let mut failures = 0;
    for solver in [&DenseLu as &dyn LinearSolver, &FaerSparseLu] {
        for step in 0..=10 {
            let mut rhs = plan.rhs().to_vec();
            rhs[plan.branch_index("drive").unwrap()] = f64::from(step) * 0.0001 / 0.001;
            let values = match solver.solve_csc(matrix, &rhs) {
                Ok(values) => values,
                Err(error) if error.code == ErrorCode::Unsupported => {
                    solver.solve(dense, &rhs).unwrap()
                }
                Err(error) => panic!("{error:?}"),
            };
            if let Err(error) = verify_csc_solution(matrix, &rhs, &values) {
                let mut sums = vec![0.; matrix.dimension()];
                let mut scales: Vec<_> = rhs.iter().map(|v| v.abs()).collect();
                for (row, column, coefficient) in matrix.entries() {
                    sums[row] += coefficient * values[column];
                    scales[row] += (coefficient * values[column]).abs();
                }
                eprintln!(
                    "provider={} step={step} error={error:?} rhs={rhs:?} values={values:?} sums={sums:?} scales={scales:?}",
                    solver.name()
                );
                failures += 1;
            }
        }
    }
    assert_eq!(
        failures, 0,
        "direct solutions must meet independent acceptance"
    );
}

proptest::proptest! {
    #![proptest_config(proptest::test_runner::Config {
        cases: 128,
        rng_seed: proptest::test_runner::RngSeed::Fixed(57120261103),
        failure_persistence: Some(Box::new(proptest::test_runner::FileFailurePersistence::Direct(
            "tests/gain_differential_transient.proptest-regressions"))),
        ..Default::default()
    })]
    #[test]
    fn affine_voltage_constraints_survive_bounded_randomized_rhs(
        gain in -8_i16..9, drive in -10_i16..11, reference in -10_i16..11,
        low in -10_i16..11, offset in -10_i16..11, load_exponent in 0_u32..7
    ) {
        use circuit_simulator::{api::Analysis, circuit::Element};
        let mut request: Request=serde_json::from_str(REQUEST).unwrap();
        request.analysis=Analysis::OperatingPoint;
        for (index,value) in [drive,reference,low,offset].into_iter().enumerate() {
            if let Element::Voltage {volts,..}=&mut request.circuit.elements[index] {
                *volts=f64::from(value)/10.;
            }
        }
        if let Element::Vcvs {gain: value,..}=&mut request.circuit.elements[4] {*value=f64::from(gain);}
        if let Element::Resistor {ohms,..}=&mut request.circuit.elements[5] {*ohms=10_f64.powi(load_exponent as i32);}
        let expected=f64::from(low)/10.+f64::from(offset)/10.
            +f64::from(gain)*(f64::from(drive)/10.-f64::from(reference)/10.);
        for backend in ["lu","faer"] {
            request.backend=backend.into();
            let result=run(&request);
            proptest::prop_assert!(result.is_ok(),"{backend}: {:?}",result.err());
            let result=result.unwrap();
            let output=result.signals.iter().position(|s|s.id=="v(out)").unwrap();
            proptest::prop_assert!((result.samples[0].values[output]-expected).abs()<=1e-11*(1.+expected.abs()));
        }
    }
}
