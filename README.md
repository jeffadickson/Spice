# Spice — Rust circuit-simulation reference engine

A bounded Rust circuit engine with CPU provider adapters and C#/F# access. This is **not a full M1 release**.

## Working scope

- Independent numerical libraries: nalgebra dense LU/QR and faer sparse LU (real and complex). The assembler still uses dense matrices and a 256-unknown cap; no large-circuit scaling claim is made.
- Immutable circuit plans, explicit capabilities, prepared numeric-factor reuse and independent residual checks.
- R/C/L and independent sources; DC, complex AC, legacy fixed-step transient, and configured backward-Euler/trapezoidal transient.
- A bounded Shockley diode with damped Newton DC, DC-biased small-signal AC and qualified backward-Euler nonlinear transient paths; broader nonlinear methods remain unsupported.
- Configured consistent initial states, PWL/sine source overrides, source corners, optional adaptive step doubling, accepted-sample observers and integration statistics.
- Unit-aware measurement/assertion results, DC power diagnostics and bounded sequential campaigns with separate case outcomes.
- Immutable scalar sweeps and bounded Cartesian grids, including reproducible seeded uniform and log-uniform value plans with unit-aware per-point results.
- One-shot JSON CLI, capability discovery, typed C# requests/results, and F# builders.
- Trusted local result artifact publication and SHA-256/byte-count verification.
- Analytic, malformed-input, seeded property/fuzz-smoke and selected mutation tests with actual coverage scope/limitations.

Independent audits now check DC KCL, voltage constraints and power, plus legacy
constant-source backward-Euler step energy/current constraints. Successful JSON
manifests include `consistency` and `diagnostics`; unavailable physical checks
and global accuracy are explicitly `not_checked`. See
[evidence and limits](evidence/consistency-qualification.md) and
[requirements](docs/consistency.sdoc). For JSON diagnostic logs on stderr, set
`$env:RUST_LOG='circuit_simulator=debug'`; stdout remains result JSON.

## Build and run

Use Rust from `rust-toolchain.toml`, .NET SDK from `global.json`, and run from this directory:

```powershell
./scripts/Test.ps1
./target/debug/circuit-simulator.exe --capabilities
Get-Content -Raw examples/adaptive-rc.json | ./target/debug/circuit-simulator.exe
dotnet run --file examples/Typed.cs -- target/debug/circuit-simulator.exe
dotnet fsi examples/TypedRlc.fsx target/debug/circuit-simulator.exe
dotnet run --file scripts/Checkpoint.cs -- 100
dotnet run --file scripts/Mutation.cs
```

For another Windows or Ubuntu computer, follow [automatic setup, testing and result-branch publication](docs/remote-runs.md).

For overnight seeded fuzz/property testing, use `make endurance` (five-hour maximum) or the PowerShell launcher; see [campaign controls and replay](docs/endurance.md).

For Proptest/FsCheck shrinking, replay and Coverlet/ReportGenerator reports, see
[generated testing instructions](docs/property-testing.md). Synthetic transport
fixture evidence remains separate from real numerical conformance.

For engineering HTML and generated managed API reference, run
`scripts/Setup-EngineeringDocs.ps1 -Python <existing-Python-3.12+-path>`.
It restores pinned Sphinx in an isolated environment and builds with warnings as
errors. Canonical StrictDoc requirements remain in docs/*.sdoc.

For the focused nonlinear diode/Newton contract suite, run `./scripts/Test-Nonlinear.ps1`.
The active campaign checkpoints are 344, 354, …, 434; see
[the campaign ledger](docs/qualification-campaign.md).
The runnable `examples/continued-bridge.json` demonstrates source continuation
with total correction-work reporting. Its bounded physical-oracle and mutation
evidence is in [checkpoint 344](evidence/continuation-344-qualification.md).

Linux executable names omit `.exe`. Build the managed library before F# examples load it. `scripts/Test-SecondSlice.ps1` runs the current build/tests and typed examples. Source/revision evidence is under `evidence/`; generated logs and binaries stay outside Git.

`scripts/Test-ManagedCoverage.ps1 -CoverageTool <path-to-dotnet-coverage>` measures the client SDK separately from its conformance executable. `scripts/Test-Coverage.ps1` selects a Rust coverage toolchain with `profiler_builtins`; this host still needs Visual C++ Build Tools and the Windows SDK for `link.exe`. Unavailable Rust coverage is explicitly recorded, never inferred from managed metrics.

## Documents

- [Architecture](docs/architecture.sdoc), [implementation plan](docs/implementation-plan.sdoc), [backlog](BACKLOG.md).
- [Original bounded profile](docs/prototype-contracts.sdoc), [second-slice requirements](docs/second-slice.sdoc), [extended acceptance contracts](docs/second-slice-contracts.sdoc).
- [Current implementation status](docs/implementation-status.md), [solver adoption experiment](docs/faer-adoption.md).
- [Iteration ledger](evidence/iterations.md), checkpoint reports, [mutation results](evidence/mutation.json), [coverage](evidence/coverage.json).

## Limits and remaining work

No GPU execution, native OSDI plugins, Arrow transport, persistent worker sandbox, C ABI, general Starlark runtime, remote service or GUI is implemented. A bounded Shockley diode/Newton path and SForge v0.1 parser/lowering path are implemented, but expression IR is not yet an integrated behavioral device. AC source magnitude/phase is supported for the selected import/API path. No general vendor-model or StressForge compatibility is claimed.

The bounded text/import frontends cover selected R/C/L/V/I, diode, controlled-source, waveform, directive and ground-alias forms. They are not a broad SPICE dialect; unsupported syntax fails explicitly.

The legacy transient path uses zero stored-energy startup and records the first positive step. The configured path starts from a DC operating point unless a full consistent MNA state is supplied, and records t=0. Adaptive tolerance controls local step-doubling estimates, not global error or unobserved waveform peaks.

The initial size/output budgets remain enforced. The local artifact API requires trusted local storage; integrity hashes are not signatures or a sandbox. Campaigns are bounded and sequential, without crash resume or distributed scheduling.

The full plan remains partially implemented. CI is configured but no remote execution is claimed. Product source licensing and distribution qualification remain open decisions.


Generated Rust/C# mutations: see [mutation framework instructions](docs/mutation-frameworks.md).
