use circuit_simulator::sampling::{LogUniformRange, LogUniformSweepPlan, SeededSequence, UniformRange, UniformSweepPlan, log_uniform_values, uniform_values};
use circuit_simulator::{api::Request, registry::BackendRegistry, sweep::run_uniform_plan};

#[test]
fn seeded_stream_is_reproducible_and_bounded() {
    let range = UniformRange { low: -2., high: 3. };
    let mut a = SeededSequence::new(42);
    let mut b = SeededSequence::new(42);
    for _ in 0..32 {
        let left = a.uniform(range).unwrap();
        assert_eq!(left, b.uniform(range).unwrap());
        assert!((-2. .. 3.).contains(&left));
    }
}

#[test]
fn uniform_recipe_executes_as_regular_scalar_sweep() {
    let request: Request = serde_json::from_str(include_str!("../examples/divider.json")).unwrap();
    let plan = UniformSweepPlan { element_id: "R1".into(), seed: 8, count: 2, range: UniformRange { low: 900., high: 1100. } };
    assert_eq!(run_uniform_plan(&request, &plan, &BackendRegistry::reference()).unwrap().len(), 2);
}

#[test]
fn log_sweep_plan_rejects_nonpositive_ranges() {
    let plan = LogUniformSweepPlan { element_id: "R1".into(), seed: 1, count: 3, range: LogUniformRange { low: 0., high: 1. } };
    assert!(plan.materialize().is_err());
}

#[test]
fn log_uniform_sampling_stays_positive_and_bounded() {
    let range = LogUniformRange { low: 1e-3, high: 1e3 };
    for value in log_uniform_values(12, 10, range).unwrap() { assert!((range.low..range.high).contains(&value)); }
    assert!(LogUniformRange { low: 0., high: 1. }.validate().is_err());
}

#[test]
fn uniform_sweep_plan_records_reproducible_materialization() {
    let plan = UniformSweepPlan { element_id: "R1".into(), seed: 7, count: 2, range: UniformRange { low: 1., high: 2. } };
    assert_eq!(plan.materialize().unwrap().values, plan.materialize().unwrap().values);
    assert_eq!(plan.provenance().algorithm, "splitmix64-v1");
}

#[test]
fn generated_sample_vectors_retain_seeded_order() {
    let range = UniformRange { low: 10., high: 20. };
    assert_eq!(uniform_values(9, 3, range).unwrap(), uniform_values(9, 3, range).unwrap());
    assert!(uniform_values(9, 0, range).is_err());
    assert!(uniform_values(9, 65, range).is_err());
}

#[test]
fn malformed_sampling_ranges_fail_before_sampling() {
    assert!(UniformRange { low: 1., high: 1. }.validate().is_err());
    assert!(UniformRange { low: f64::NAN, high: 1. }.validate().is_err());
}
