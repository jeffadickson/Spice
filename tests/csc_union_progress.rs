//! Bound the isolated liveness probe so a merge regression becomes a test failure.
use circuit_simulator::sparse::CscPattern;
use proptest::prelude::*;
use proptest::test_runner::{Config, FileFailurePersistence, RngSeed, TestRunner};
use std::{
    collections::BTreeSet,
    process::{Child, Command, Stdio},
    time::{Duration, Instant},
};

struct OwnedProbe(Child);
impl Drop for OwnedProbe {
    fn drop(&mut self) {
        // Always reap our own subprocess, including assertion/error paths.
        let _ = self.0.kill();
        let _ = self.0.wait();
    }
}

#[test]
fn csc_union_completes_within_a_bounded_probe() {
    let mut child = OwnedProbe(
        Command::new(std::env::current_exe().unwrap())
            .args(["--exact", "csc_union_probe_child", "--nocapture"])
            .env("SPICE_CSC_UNION_PROBE", "1")
            .stdout(Stdio::null())
            .spawn()
            .unwrap(),
    );
    // A harness liveness bound, not a general solver performance guarantee.
    let deadline = Instant::now() + Duration::from_secs(3);
    loop {
        if let Some(status) = child.0.try_wait().unwrap() {
            assert!(status.success(), "isolated CSC union contract failed");
            break;
        }
        assert!(
            Instant::now() < deadline,
            "CSC union probe exceeded three seconds"
        );
        std::thread::sleep(Duration::from_millis(5));
    }
}

fn pattern(n: usize, mask: &[bool]) -> CscPattern {
    let mut pointers = vec![0];
    let mut rows = Vec::new();
    for column in 0..n {
        for row in 0..n {
            if mask[column * n + row] {
                rows.push(row);
            }
        }
        pointers.push(rows.len());
    }
    CscPattern::new(n, pointers, rows).unwrap()
}

#[test]
fn csc_union_probe_child() {
    if std::env::var("SPICE_CSC_UNION_PROBE").as_deref() != Ok("1") {
        return;
    }
    // The right column is empty but the next column starts with a smaller row.
    // Reading that next-column row here prevents either iterator from advancing.
    let left = CscPattern::new(2, vec![0, 1, 1], vec![1]).unwrap();
    let right = CscPattern::new(2, vec![0, 0, 1], vec![0]).unwrap();
    let union = left.union(&right).unwrap();
    assert_eq!(union.col_ptr(), &[0, 1, 2]);
    assert_eq!(union.rows(), &[1, 0]);
    let config = Config {
        cases: 256,
        rng_seed: RngSeed::Fixed(2026103001),
        failure_persistence: Some(Box::new(FileFailurePersistence::Direct(
            "tests/regressions/csc_union_progress.txt",
        ))),
        ..Default::default()
    };
    let strategy = (1usize..=8).prop_flat_map(|n| {
        (
            Just(n),
            proptest::collection::vec(any::<bool>(), n * n),
            proptest::collection::vec(any::<bool>(), n * n),
        )
    });
    TestRunner::new(config)
        .run(&strategy, |(n, a, b)| {
            let a = pattern(n, &a);
            let b = pattern(n, &b);
            let expected: BTreeSet<_> = a.slots().chain(b.slots()).collect();
            let actual = a.union(&b).unwrap();
            prop_assert_eq!(actual.nnz(), expected.len());
            prop_assert_eq!(actual.slots().collect::<BTreeSet<_>>(), expected);
            prop_assert_eq!(actual.col_ptr().len(), n + 1);
            Ok(())
        })
        .unwrap();
}
