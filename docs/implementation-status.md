# Implementation status

Latest five-area checkpoints are described in [the execution plan](five-area-completion-plan.md)
and `evidence/five-area-{transport,benchmark,continuation,sparse,audits}.json`.
They supersede the older managed counts and missing-audit descriptions below.
The former three deliberate transport timeouts are caught by test failures;
benchmark timing is accuracy-gated; coupled continuation, sparse RC/RLC and
fixed-step/linear-AC physical audits have finite evidence. Requirements remain
Draft outside their bounded profiles; full M1 work packages remain Proposed.
New Rust line coverage is focused nightly MSVC evidence, with branch coverage
unavailable. New Linux numerical validation is unavailable after disk/WSL failures;
earlier Linux reports are historical. Remaining managed/benchmark survivors are
explicitly retained, and no new full-duration soak or production claim is made.

Historical checkpoints follow.
Latest managed follow-up: [October 30 mutation dispositions](../evidence/managed-20261030-review.md).
Windows final native counts are 181 killed, three survivors, three timeouts, one
compiler error, one uncovered and 13 ignored (202 generated). Focused Linux
cleanup evidence catches the Windows exit-wait survivor. The 80-test managed
suite passes on both platforms; three deliberate liveness timeouts, interactive
Windows console qualification, the defensive startup-null branch and pre-ack
fixture startup-fault cleanup remain follow-up work. BOM equivalence is conditional.


Latest finite follow-up: [October 30 fixes and evidence](../evidence/soak-20261030-fixes-review.md).
The fd34c53 six-hour campaign completed; a full-duration run on these new fixes remains pending.
Managed mutation gaps remain explicitly tracked in that report; earlier coverage counts below are historical.

The 100-step qualification campaign (repository iterations 335–434) is complete;
final review/fixes 435 are complete with recorded verification. The StrictDoc implementation plan's full
work packages retain Proposed status; these finite slices do not close M1 release
gates. Use the latest checkpoint/evidence hashes rather than historical counts.

| Area | Implemented and finitely tested | Remaining |
|---|---|---|
| Convergence | bounded analytic diode Newton, dimensioned norms, source continuation, fixed/staged Gmin and mandatory physical final solve; terminal faults and stagnation checks | broader difficult topology/model corpus and general convergence guarantees |
| CPU sparse | canonical CSC DC/complex AC/linear BE/trapezoidal, owned faer real factors, exact-step cache, independent residuals and selected provider failures | general rank diagnostics, prepared complex reuse, broader matrix/performance qualification |
| Models | R/C/L, controlled/independent sources, Shockley diode and explicit constant parallel Q=C*V diode profile; equation/derivative/domain/unit/history/audit checks | depletion/transport/temperature-dependent IS, other semiconductors and full plugin model API |
| Dynamics | configured initial constraints, PWL/sine and bounded other source imports, linear BE/trapezoidal and nonlinear BE, adaptive step doubling and accepted observers | nonlinear trapezoidal, complete DAE/event/co-simulation and global error bounds |
| API | typed C#/F# experimental JSON, optional complex excitation, additive typed charge profile, measurements/assertions, descriptors and capability limits | complete stable API/migration policy, Arrow, C ABI and persistent isolated worker |
| Oracles | selected attributed upstream fixtures plus original named AC/transient cases through LU/faer; explicit temperature, interpolation and rounding bounds | broad ngspice language/device compatibility and larger independently curated corpus |
| Consistency | independent DC KCL/source/power, linear AC complex power/current, legacy BE and configured fixed BE/linear trapezoidal energy/source audits; bounded topology checks, provider output acceptance and stderr tracing | nonlinear AC without retained bias, adaptive physical intervals without accepted midpoint history, complete topology/rank and rigorous global accuracy |
| Verification | Proptest/FsCheck shrinking/replay, maintained cargo-mutants/Stryker, malformed/negative cases, scoped LLVM/Coverlet/helper coverage, StrictDoc/Sphinx | coverage-guided fuzzing, native branch metrics, broader host/helper/vendor qualification |
| Remote campaigns | configurable budgets, Windows/Linux PowerShell setup, immutable provenance, bounded result branches and local raw artifacts | packaged release, broader interrupted-run recovery and completed current five-hour evidence |

SForge v0.1 remains a bounded primitive-transient authoring/lowering path; its
proposal is broader than implemented behavior. Campaigns remain bounded/sequential;
no general parallel numerical scheduling or production plugin sandbox is implied.

Final evidence: [final-435-review.md](../evidence/final-435-review.md).
Fresh measured scopes are Rust 5342/5783 owned lines, client 166/174 lines
and 109/112 branches; native branches are unavailable. Counts and scopes are
separate from correctness, whole-project qualification and vendor coverage.

The historical e85c1e2 one-hour run completed 3599.04 seconds and 50,987,008
custom seeded cases with 22/22 selected custom mutants; it predates framework
integration. Two earlier partial failures remain preserved. Never relabel that
hour as cargo-mutants/Stryker evidence. Current campaigns use maintained mutation
frameworks then library property batches; timeouts, survivors and unviable mutants
remain distinct from caught mutants.

Windows Build Tools/SDK installation and native link/AOT fixture probes succeeded;
Windows instrumented Rust coverage remains unmeasured. WSL native Rust coverage
and Windows/Linux functional managed tests have actual checkpoint reports.

No full SPICE parity, GPU implementation, universal convergence, global error
bound, large-circuit speedup or production qualification is claimed. Raw campaign
artifacts remain local; completed checkpoint commits are pushed without CI dispatch.
# October 28 verification follow-up

See [the survivor review](../evidence/overnight-20261028-review.md): the new
22-survivor Rust cohort has 20 detected kills and two conditional equivalents.
Remaining verification work includes the full-suite managed EOF timeout,
Stryker RunJsonAsync definite-assignment rollback (103 compile-error candidates),
and incomplete adaptive-module LLVM mappings. Focused kills, unavailable mutation
coverage and conditional equivalents remain separate outcomes. None establishes
general M1, GPU or production qualification.

## October 29 survivor follow-up

The [new cohort review](../evidence/overnight-20261029-review.md) records 22
test-detected kills among 24 original survivors, one downstream-masked ID guard
and one conditional pulse-guard equivalent. Source/test checkpoint 3e6d8f0 adds
17 tests; Windows and Linux each passed 396 tests with one intentional ignore.
LLVM mapping warnings, the full-suite managed EOF timeout and Stryker rollback
remain open verification gaps. Reopen the masked guard if failure-phase or clone
resource policy changes; do not count it as a semantic equivalent or kill.
