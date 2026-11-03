//! Minimized failing circuit from the November 3 endurance property campaign.
use circuit_simulator::{
    backend::DenseLu,
    backend_faer::FaerSparseLu,
    circuit::{Circuit, Element},
    compile::compile,
    dc::operating_point_detailed,
    error::SimResult,
    ids::NodeId as N,
    linalg::{LinearSolver, Matrix},
};
struct Recorded<'a> {
    inner: &'a dyn LinearSolver,
    calls: std::sync::atomic::AtomicUsize,
}
impl LinearSolver for Recorded<'_> {
    fn name(&self) -> &'static str {
        self.inner.name()
    }
    fn solve(&self, m: &Matrix, rhs: &[f64]) -> SimResult<Vec<f64>> {
        let d = self.inner.solve(m, rhs);
        let n = self
            .calls
            .fetch_add(1, std::sync::atomic::Ordering::Relaxed);
        println!("{} correction{n} rhs={rhs:?} delta={d:?}", self.name());
        d
    }
}
#[test]
fn minimized_midpoint_switch_stagnation() {
    let drive = 1.5822841689813791;
    let ron = 10.;
    let roff = 10000.;
    let load = 100.;
    let c = Circuit {
        nodes: vec!["0".into(), "in".into(), "out".into(), "control".into()],
        elements: vec![
            Element::Voltage {
                id: "drive".into(),
                p: N(1),
                n: N(0),
                volts: drive,
            },
            Element::Voltage {
                id: "control".into(),
                p: N(3),
                n: N(0),
                volts: 0.5,
            },
            Element::PspiceSwitch {
                id: "switch".into(),
                p: N(1),
                n: N(2),
                cp: N(3),
                cn: N(0),
                ron,
                roff,
                von: 0.,
                voff: 1.,
            },
            Element::Resistor {
                id: "load".into(),
                p: N(2),
                n: N(0),
                ohms: load,
            },
        ],
    };
    let plan = compile(&c).unwrap();
    let expected = drive * load / (load + (ron * roff).sqrt());
    let mut failures = vec![];
    for solver in [&DenseLu as &dyn LinearSolver, &FaerSparseLu] {
        let r = Recorded {
            inner: solver,
            calls: std::sync::atomic::AtomicUsize::new(0),
        };
        let result = operating_point_detailed(&plan, &r);
        println!("{} expected={expected} result={result:?}", solver.name());
        match result {
            Ok((state, report)) => {
                assert!((state[1] - expected).abs() <= 1e-8 + 1e-8 * expected.abs());
                assert!((state[0] - drive).abs() < 1e-12);
                assert!((state[2] - 0.5).abs() < 1e-12);
                assert!((state[3] + expected / load).abs() < 1e-12);
                assert!(
                    !circuit_simulator::audit::dc(&c, &state, Default::default(), None)
                        .unwrap()
                        .has_failures()
                );
                assert!(report.unwrap().iterations <= 50);
            }
            Err(e) => failures.push((solver.name(), e)),
        }
    }
    assert!(failures.is_empty(), "{failures:?}");
}

/// Prepared PNP Ebers–Moll fixture from the existing managed continuation test.
/// Zero source constraints are already feasible; recovery must keep legacy KCL descent.
#[test]
fn pnp_source_and_gmin_recovery_keep_eight_iteration_stages_on_both_providers() {
    use circuit_simulator::{
        api::{self, Analysis, Request},
        circuit::Element,
        newton::{ContinuationOptions, GminContinuationOptions, NewtonOptions},
    };
    let base: Request =
        serde_json::from_str(include_str!("fixtures/pnp_gmin_20261103.json")).unwrap();
    let vt = 1.38064852e-23 * 300.15 / 1.6021766208e-19;
    let expected = -vt * (1e-6_f64 / (1e-15 * 1.01)).ln_1p();
    let mut failures = vec![];
    for backend in ["lu", "faer"] {
        for gmin in [false, true] {
            let mut request = base.clone();
            request.backend = backend.into();
            request.analysis = Analysis::OperatingPointNewton {
                options: NewtonOptions {
                    max_iterations: 8,
                    continuation: (!gmin).then_some(ContinuationOptions {
                        initial_step: 0.125,
                        minimum_step: 1e-12,
                        max_stages: 128,
                    }),
                    gmin_continuation: gmin.then_some(GminContinuationOptions {
                        initial_siemens: 1.,
                        minimum_siemens: 1e-9,
                        reduction_factor: 0.5,
                        max_stages: 64,
                    }),
                    ..Default::default()
                },
            };
            match api::run(&request) {
                Ok(result) => {
                    let voltage = result.samples[0].values[0];
                    assert!((voltage - expected).abs() < 2e-7);
                    assert!((1e-15 * 1.01 * (-voltage / vt).exp_m1() - 1e-6).abs() < 2e-12);
                    let report = result.newton.unwrap();
                    assert_eq!(report.gmin_siemens, None);
                    if gmin {
                        assert!(report.gmin_continuation.unwrap().final_unregularized);
                    }
                    assert!(!result.consistency.has_failures());
                }
                Err(error) => failures.push(format!("{backend} gmin={gmin}: {error:?}")),
            }
            // Reversing the supply has no physical forward-junction solution.
            for element in &mut request.circuit.elements {
                if let Element::Current { amps, .. } = element {
                    *amps = -*amps;
                }
            }
            assert!(api::run(&request).is_err());
        }
    }
    assert!(failures.is_empty(), "{}", failures.join("\n"));
}
