# Overnight survivor review — 2026-10-27

The original 35 Rust survivors are individually tracked in
[the stable inventory](overnight-survivor-triage.json): **31 are now caught and
four remain as conditional, source-reviewed equivalents**. Each caught outcome
includes an actual failing-test name, native log path, report checksum and tested
source checksum. This is a deliberately selected survivor cohort, not a new
whole-project mutation score or a formal equivalence proof.

The source checkpoints are `d77bd6d` (campaign/publication controls) and
`216859d786eeabaacd549f33c8a2675bbdb0c167` (numerical work accounting and tests).
[Replay evidence](triage-survivor-replay.json) records two completed runs: 31
selections with `lib,overnight_survivors`, then eight with
`lib,overnight_survivors,ngspice_compat`. Their 119 copied files match each other
and the current checkout byte-for-byte. Two supplemental importer mutations
overlap the runs; original-cohort outcomes are counted once. The expanded run
caught both extras through existing importer tests. Their earlier survival was
an omitted-suite effect, not a newly discovered missing assertion.

The new tests exposed a genuine defect: nonlinear transient initialization could
perform six provider solves while reporting one. `configured.rs` now retains the
Newton initialization count. An independent provider-call counter checks implicit
and explicit initial states and adaptive work. Nineteen integration regressions
and three private numerical contracts also challenge input/resource boundaries,
controlled-source polarity, floating-terminal diode AC, Richardson error and step
growth, dense acceptance products, waveform edges, diagnostics and reproducibility.
The SplitMix constants were separately checked by compiling the public-domain
[reference C implementation](triage-splitmix-reference.json); this is a three-vector
check, not statistical generator qualification.

The retained Rust dispositions are deliberately visible:

| Stable ID | Current-path reasoning |
| --- | --- |
| RUST-SURV-020 | Exact measurement times return before the interpolation bracket predicate; validated axes exclude duplicate times. |
| RUST-SURV-025 | Equality at the limiter ceiling cannot change an existing finite scale; the inventory also records the overflow/NaN case and standard-library semantics. |
| RUST-SURV-027 | Backtracking already reports damping through a scale below one, masking the additional attempt predicate. |
| RUST-SURV-030 | Circuit validation rejects identical voltage-source terminals before topology canonicalization. |

These are source reviews under current runtime, validation and caller assumptions.
Reopen them when source hashes or preconditions change. No mutation suppression
or acceptance-tolerance relaxation was added. The unchanged Stryker survivor is
[separately reviewed](overnight-managed-survivor-triage.json): its BOM-emission
constructor flag is unused by this decode-only path. Stryker.NET was not rerun in
this follow-up; the original 89 caught, one survivor, 103 compile errors and seven
ignored outcomes remain historical categories, not new detection results.

[The original overnight review](triage-overnight-run-review.json) retains source
`742341b`, 12,449,792 successful requested property cases in 6,079 batches, initial
seed `6272588092136166575` and last completed seed `3971496431024587951`. Its
22,566.51 seconds exceed the 18,000-second budget by 4,566.51 seconds. A Windows
power event confirms suspension from 10:00:57Z to 13:22:01Z. Its recorded
`budget_exhausted` status therefore does not establish five active testing hours.
No property failure was found, and no fresh coverage was collected by that run.

The supervisor now maintains a nondecreasing elapsed-clock budget, checks UTC and
monotonic deadlines after wake, excludes late completed batches from completed
counts and reports excessive cleanup overruns as failures. It acquires and releases
a temporary Windows system-required request without changing persistent policy;
explicit sleep and OS policy can override it. Linux sleep prevention remains
unavailable. Publication independently rejects over-budget evidence as passing.
LF JSON serialization fixes the reproduced Windows Git-blob checksum defect.
Five old Windows manifests remain flagged historical mismatches; no result branch
was rewritten.

An early replay was interrupted by Windows allocation failures, including error
1455 and linker exhaustion. Its seven caught, three native-unviable and 27
unattempted outcomes are preserved in [the interruption record](triage-resource-interruption.json)
and excluded from the final cohort totals. Those three compile failures were
infrastructure failures requiring replay. All 86 original overnight unviable logs
were also scanned: none contained the explicit allocation signatures checked;
that absence does not establish intrinsic invalidity or test detection.

Fresh verification is bounded and reproducible:

- Windows and Linux: 367 Rust tests passed; one legacy endurance test is ignored
  by the ordinary suite. Each platform requested 16,384 Proptest cases using seed
  `202610270815`; these repeated inputs are not 32,768 distinct cases. Replay and
  shrink attempts are not estimated. Existing malformed-input/fuzz-smoke tests ran.
- `cargo fmt --check` and `cargo clippy --all-targets -- -D warnings` passed.
  Cargo-nextest was unavailable in the active environment, so `cargo test` ran.
- Pester 3.4.0: 21 tooling contracts passed, including real synthetic bare-Git
  publication, clock scenarios, Windows power-request release, native inventory
  checks and synthetic orchestration. Synthetic counters are not simulator evidence.
- StrictDoc parsed 14 documents with 260 unique IDs and 247 resolved parents;
  Sphinx built with warnings treated as errors and projected 230 XMLDoc entries.
  REM-004, END-005, MUT-005 and INT-WORK-001 remain Draft requirements.

| Measured scope | Line coverage | Branch coverage |
| --- | --- | --- |
| Owned Rust source, Linux LLVM | 5,425 / 5,853 (92.69%) | Unavailable |
| Endurance C# tooling, Windows | 221 / 255 (86.67%) | 134 / 208 (64.42%) |
| Remote publication C# tooling, Windows | 125 / 162 (77.16%) | 196 / 272 (72.06%) |
| Framework supervisor C# tooling, scoped contracts | 106 / 180 (58.89%) | 142 / 264 (53.79%) |
| Survivor relocation helper | 32 / 32 (100%) | 26 / 36 (72.22%) |
| Coverage summary helper | 26 / 26 (100%) | 30 / 34 (88.24%) |

Reports: [Rust](triage-rust-coverage.json), [tooling](triage-tooling-coverage.json),
[helpers](triage-helper-coverage.json), [test accounting](triage-rust-regression.json)
and [tooling contracts](triage-tooling-tests.json). The 79 LLVM mismatch records
match five previously attributed zero-hash tracing/tracing_core symbols; no owned
or new unclassified mismatch was found. Duplicate managed branch mappings use
per-line maxima. Vendor internals, PowerShell branch metrics, GPU/native plugins,
unexecuted platform/error paths and fresh client-library coverage are excluded.
No coverage percentage establishes numerical correctness or production readiness.

Read the checked replay command in [mutation-frameworks.md](../docs/mutation-frameworks.md).
Raw artifacts remain local. Source changes are locally committed; this follow-up
has not pushed them or dispatched GitHub CI. The existing nightly schedule remains
unchanged and must provide fresh long-duration confirmation of these controls.
Full SPICE, general GPU and production qualification remain unclaimed.
