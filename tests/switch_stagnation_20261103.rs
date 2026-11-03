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
