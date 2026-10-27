# Implementation status

The 100-step qualification campaign (repository iterations 335–434) is complete;
final review/fixes 435 are being verified. The StrictDoc implementation plan's full
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
| Consistency | independent DC KCL/source/power and legacy BE energy audits, bounded topology checks, provider output acceptance and stderr tracing | automatic AC/configured physical audits, complete topology/rank and rigorous global accuracy |
| Verification | Proptest/FsCheck shrinking/replay, maintained cargo-mutants/Stryker, malformed/negative cases, scoped LLVM/Coverlet/helper coverage, StrictDoc/Sphinx | coverage-guided fuzzing, native branch metrics, broader host/helper/vendor qualification |
| Remote campaigns | configurable budgets, Windows/Linux PowerShell setup, immutable provenance, bounded result branches and local raw artifacts | packaged release, broader interrupted-run recovery and completed current five-hour evidence |

SForge v0.1 remains a bounded primitive-transient authoring/lowering path; its
proposal is broader than implemented behavior. Campaigns remain bounded/sequential;
no general parallel numerical scheduling or production plugin sandbox is implied.

Checkpoint 434 evidence: [models-434-qualification.md](../evidence/models-434-qualification.md).
Fresh measured scopes there are Rust 5310/5750 owned lines, client 166/174 lines
and 109/112 branches; native branches are unavailable. These are checkpoint
metrics, not automatically the metrics for later review edits.

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
