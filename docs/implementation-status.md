# Implementation status after iterations 201–301

Current checkpoint 374 completes campaign steps 1–40. See
[qualified scope and remaining findings](../evidence/testing-374-qualification.md).
Source/Gmin hardening and maintained generated-test workflows are implemented;
direct sparse AC has an implemented first tranche (375–383): canonical dynamic
union, genuine complex RHS, direct linear/diode incremental CSC and independent
backward-error checks. Checkpoint 384 measurement is pending; wider named AC
oracles, sparse transient and further model qualification remain active.

Testing campaign 355–364 adds Proptest/FsCheck library generation and shrinking,
isolated subprocess contracts for the .NET client, and Coverlet/ReportGenerator.
See [replay instructions](property-testing.md); checkpoint 374 migrated overnight
batches to Proptest and made maintained mutation frameworks primary. Full
SPICE/GPU/production qualification remains open.

Earlier checkpoint: iterations 335–344 harden continuation/provider
boundaries and add independent coupled-diode oracles. See
[checkpoint evidence](../evidence/continuation-344-qualification.md) and
[the active campaign](qualification-campaign.md). Full checkpoint: 257 Rust tests,
29 managed checks; nine focused cargo-mutants survivors remain unresolved.

The fifth slice adds **bounded fixed-step nonlinear diode transient stepping** using backward-Euler residuals and analytic damped Newton iterations. It does not complete the 23-package production plan.

| Plan area | Implemented evidence | Remaining |
|---|---|---|
| Solver variety | nalgebra dense and independent faer sparse LU; real/complex conformance, prepared RHS reuse, compiler-owned canonical CSC stamps, and direct faer use for linear DC | broad difficult-matrix corpus, native KLU/cuDSS/Ginkgo adapters, sparse AC/transient and large-circuit qualification |
| Device equations | R/C/L, a Shockley diode, independent sources, signed flux and branch currents, DC power diagnostics, DC-biased diode small-signal AC, bounded DC source continuation, explicit/staged Gmin, dimension-aware DC checks, and current-driven continuation recovery/physical-final-solve evidence | additional semiconductor models, broader difficult-circuit qualification, production limiting/Jacobians and full device/plugin API |
| Dynamic analyses | configured initial states, PWL/sine sources, trapezoidal/BE, adaptive step doubling, accepted observers, fixed-step and configured backward-Euler diode Newton including source overrides/breakpoints | full event/co-simulation rules and complete DAE topology qualification |
| API/data | typed C#/F# experimental JSON contract, measurements/assertions, capability discovery | Arrow, C ABI, persistent sandboxed worker, complete stable API/migration policy |
| Campaigns | bounded sequential cases, scalar sweeps/grids, seeded sampling recipes, per-point failures and programmatic result rows | parallel resource scheduling, statistical qualification, retries/resume, batch CLI transport, actual StressForge contract |
| Persistence | bounded trusted-local artifact commit/read with hashes | hostile storage policies, remote/object storage, power-loss durability qualification |
| Authoring | typed .NET/F# builders, SForge v0.1 lexer/parser/validator and lowering of primitive transient circuits, observables and bounded assertions | full units system, broader expression grammar, selected Starlark frontend and integrated behavioral models |
| Consistency | independent DC KCL/source/power checks, legacy BE discrete-energy checks, bounded topology diagnostics, typed manifests and stderr tracing | AC/configured-transient physical audits, full topology rank diagnostics and rigorous global accuracy bounds |
| Verification | analytic/provider/metamorphic/fuzz-smoke/mutation tests; scoped managed coverage | broader host/branch coverage, coverage-guided fuzzing, full native/GPU evidence |
| Release | no production release claim | license/platform/packaging and all declared M1 exit gates |

Most work packages retain Proposed status because their complete exit criteria are not satisfied. Current checkpoint and source-hash reports, rather than implementation counts, define what has been tested.

SForge has an implemented v0.1 parser/lowering path; [the SForge DSL proposal](modern-dsl-proposal.md) remains the wider design artifact, not a claim of general SPICE compatibility.

Portable qualification tooling (iterations 323–329) provides Windows/Ubuntu-Debian PowerShell setup, configurable bounded campaigns, provenance validation and automatic isolated result-branch publication. See [remote-run instructions](remote-runs.md) and [qualification evidence](../evidence/remote-workflow-qualification.md). Ubuntu 24.04 WSL2 owned Rust source line coverage is 89.95%; branch coverage is unavailable. Windows GNU and WSL short workflows ran; successful Windows MSVC installation and a completed full-hour/nightly campaign remain unqualified. A live checkpoint contention probe recovered 13 retries and completed 510,976 cases; this checks evidence writing rather than numerical accuracy.


Iteration 330 adds pinned generated mutation tooling: cargo-mutants for Rust and Stryker.NET for managed Client.cs behavior, backed by discoverable xUnit real-engine contracts. Nightly and remote runs reserve a bounded framework phase before seeded fuzz/property batches. See [tooling instructions](mutation-frameworks.md) and [actual scoped evidence](../evidence/framework-mutation-qualification.md). Generated candidates, kills and other outcomes remain distinct; neither the prior 22/22 suite nor the successful one-hour pre-framework run establish broad mutation coverage. The Windows Build Tools/SDK linker prerequisite has now been installed and independently smoke-tested; Windows Rust instrumentation metrics remain pending.
