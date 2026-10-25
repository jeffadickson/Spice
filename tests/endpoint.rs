use circuit_simulator::{
    backend::DenseLu,
    backend_faer::FaerSparseLu,
    compile::compile,
    configured::{self, Method, TransientConfig},
    linalg::LinearSolver,
    netlist::parse,
};

/// A repeated-addition clock previously generated a spurious sub-attosecond final step.
#[test]
fn fixed_grid_does_not_add_an_ill_conditioned_endpoint_step() {
    let plan = compile(&parse("title\nC1 a 0 1u\nR1 a b 10\nL1 b 0 1m").unwrap()).unwrap();
    let options = TransientConfig {
        stop_s: 0.0002,
        max_step_s: 1e-6,
        method: Method::Trapezoidal,
        initial_state: Some(vec![1., 1., 0.]),
        ..Default::default()
    };
    for solver in [&DenseLu as &dyn LinearSolver, &FaerSparseLu] {
        let result = configured::run(&plan, solver, &options, &false.into()).unwrap();
        assert_eq!(result.report.accepted_steps, 200);
        assert_eq!(result.samples.last().unwrap().time_s, options.stop_s);
        assert!(
            result
                .samples
                .windows(2)
                .all(|w| w[1].time_s - w[0].time_s > 0.9e-6)
        );
        let alpha = 5000_f64;
        let wd = (1e9 - alpha * alpha).sqrt();
        let expected = (-alpha * options.stop_s).exp()
            * ((wd * options.stop_s).cos() + alpha / wd * (wd * options.stop_s).sin());
        assert!((result.samples.last().unwrap().values[0] - expected).abs() < 0.001);
    }
}
