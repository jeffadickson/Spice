# November 3 overnight recovery

The three targeted Rust survivors are now caught. The Windows fixture path
failure is corrected using short physical artifact paths and a real process
preflight. The investigation also found and corrected shared-deadline and
Newton-globalization interactions; earlier failed results remain unchanged.

All new bulk builds, mutation/coverage output, logs, numerical responses and
campaign snapshots are under `E:/Artifacts/Spice-fix-20261103T140559Z`. Small Git
source checkouts and the NTFS junction-source fixtures remain source-side; no
existing artifact was moved or deleted.

| Checkpoint | Actual evidence |
|---|---|
| Three historical Rust survivors | All three compiled mutants caught; no production change needed for these test gaps |
| New regression inputs | Artifact 128/129/130 byte boundaries, inclusive 1 S Gmin, nonzero common-mode switch control, malformed/negative inputs, fixed seed and minimized replay |
| Windows paths | Real fixture fails through a logical path over 260 characters and succeeds through its resolved physical E path |
| Framework deadline | Nine path/deadline tests; one cancellation deadline covers input, exit and pipe drainage; expired budgets refuse dispatch |
| Launcher | Thirteen contracts cover canonical locks, shorter absolute/relative windows, bootstrap, cleanup and report failure |
| Managed client | 84 tests passed in the quiet covered replay; earlier profiler deadline failure retained |
| Final full native regression | 532 passed, zero failed, two explicitly ignored; exact corrected source, not the rejected prototype |
| Final frontend/native oracles | 479 passed, zero failed, nine unavailable external-prerequisite checks skipped |
| Switch numerical reference | Final engine LU/faer and ngspice47+ agree with the independent 0.3801486345140583 V / -0.003801486345140583 A result |
| Final property preflight | 44 completed batches / 101,376 completed requested cases; normal terminal `budget_exhausted`, no pending batch or recorded numerical failure |

The independent reviews are simulated coding-agent reviews, not external human
expert participation. Source/evidence inspection found and closed two important
issues: exact-byte versus Git-newline provenance, and a fresh fixture timeout
that initially ignored the remaining campaign budget.

## What changed

`FrameworkMutations.cs` keeps source/lock identity separate from its physical
mutable work root, records both, and checks the actual fixture apphost before
Stryker. `Run-VerifiedSoak.ps1` exports only the owned child's physical `fm` root.
No registry setting, drive substitution or global mutable build directory was
changed. Deep physical overrides fail with an actionable path limit.

The launcher and worker now share an absolute effective deadline. Five seconds
are reserved for cleanup inside that deadline, so source/bootstrap time cannot
silently give the worker a later clock. Windows windows of five seconds or less
refuse before dispatch.

The first property preflight exposed reversed-switch Newton stagnation on both
providers. Physically scaled row merit restores progress while voltage-defined
linear constraints are infeasible. Once feasible, the controller retains its
original scaled-residual descent. Final residual/update tolerances, iteration
budgets, provider verification and Gmin removal remain unchanged. The first
unconditional-merit prototype regressed PNP Gmin recovery; that result is retained,
and the final policy restores the original eight-correction tests on both CPUs.
This is a bounded local policy informed by KINSOL scaling guidance, not adoption
or qualification of that library.

## Campaign evidence and limits

The original night failed before any property batch: [issue30](https://github.com/jeffadickson/Spice/issues/30).
The first recovery preflight failed after 6,912 completed cases and retained its
counterexample: [issue31](https://github.com/jeffadickson/Spice/issues/31).
The second was interrupted by mismatched bootstrap clocks with 214,272 completed
cases in its last checkpoint: [issue32](https://github.com/jeffadickson/Spice/issues/32).
The final clean run returned before the shared outer deadline:
[issue33](https://github.com/jeffadickson/Spice/issues/33).
Different sources and terminal states must not be combined into one passing soak.

The final eight-minute request had 466.901 effective seconds after bootstrap and
cleanup reserve. Its terminal budget exhaustion is bounded execution evidence,
not completion of a full night or proof of correctness. Maintained mutations were
explicitly skipped in that run and exercised separately. All 385 source snapshot
hashes were verified, and no unfinished batch was credited.

Coverage remains scoped: the survivor integration exercises artifact.rs 83/99
and audit.rs 217/440 mapped lines; stable Rust supplies no measured branch maps.
The final two-case Newton integration exercises 530/898 mapped newton.rs lines;
its 59.02% file coverage excludes unit-test and vendor source and is not a full
suite percentage.
The final path/deadline helper collection covers 136/328 lines and 134/434 mapped
branches. Launcher coverage is 92/104 mapped lines with no measured branches.
Coverlet's frontend totals omit captured lowering helpers. None is a whole-product
score, and the narrow Stryker proof does not close the full managed mutation set.
The original night still has 220 unattempted Rust candidates in its historical
report. No new coverage-guided fuzz, Linux, GPU, full-SPICE or production
qualification is claimed.

The all-target Clippy attempt found an existing single-element-loop lint in
unchanged `tests/spice_diode.rs`. That diagnostic is retained; it is not silently
suppressed or represented as an all-target lint pass. Changed-scope lint results
are recorded separately in the final native evidence.

Reproduction and exact source/tool/hash records are in the adjacent requirements
and evidence files: `overnight-survivors-20261103.sdoc`,
`framework-physical-paths.sdoc`, `newton-merit-20261103.sdoc`,
`evidence/rust-survivors-20261103.md`, `evidence/framework-deadline-followup.json`,
`evidence/launcher-shared-deadline.json` and the three recovery-run manifests.
