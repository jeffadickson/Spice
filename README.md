# Spice — Rust circuit-simulation reference engine

A bounded Rust circuit engine with CPU provider adapters and C#/F# access. This is **not a full M1 release**.

The separate [parser-only frontend](docs/parser-only-results.md) uses generated
ANTLR C# recognition and tests the sample inventory without executing circuits.
See the [three-parser comparison and framework decision](docs/parser-only-plan.md)
and [declared grammar inventory](docs/parser-grammar-inventory.md). Structural
acceptance, complete syntax conformance and numerical support are distinct.

## Working scope

Run the [whole upstream corpus matrix](docs/ngspice-corpus-matrix.md) to learn what
is missing: every discovered deck gets a result, one GitHub results issue is
created per run, and the burndown retains unsupported/failed/unfinished work.
The five paired benchmark cases are a separate, much smaller scope.

- Independent numerical libraries: nalgebra dense LU/QR and faer sparse LU (real and complex). Qualified AC and linear transient paths use direct CSC; the reference plan retains dense compatibility matrices and a 256-unknown cap. No large-circuit scaling claim is made.
- Immutable circuit plans, explicit capabilities, prepared numeric-factor reuse and independent residual checks.
- R/C/L and independent sources; DC, complex AC, legacy fixed-step transient, and configured backward-Euler/trapezoidal transient.
- A bounded Shockley diode with damped Newton DC, DC-biased small-signal AC and qualified backward-Euler nonlinear transient paths; broader nonlinear methods remain unsupported.
- Configured consistent initial states, PWL/sine source overrides, source corners, optional adaptive step doubling, accepted-sample observers and integration statistics.
- Unit-aware measurement/assertion results, DC power diagnostics and bounded sequential campaigns with separate case outcomes.
- Immutable scalar sweeps and bounded Cartesian grids, including reproducible seeded uniform and log-uniform value plans with unit-aware per-point results.
- One-shot JSON CLI, capability discovery, typed C# requests/results, and F# builders.
- Trusted local result artifact publication and SHA-256/byte-count verification.
- Analytic, malformed-input, seeded property/fuzz-smoke and selected mutation tests with actual coverage scope/limitations.

Independent audits check DC KCL, voltage constraints and power; linear AC complex
current/power balance; and fixed BE/trapezoidal discrete energy and source constraints.
Successful JSON
manifests include `consistency` and `diagnostics`; unavailable physical checks
and global accuracy are explicitly `not_checked`. See
[physical audit scope](docs/physical-audits.md),
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

The runnable `examples/sparse-decay.json` uses direct faer CSC preparation on an
exact 0.25-second grid: BE decay is (24/25)^k, with one factor and three reuses.
It is a finite discrete-equation example, not a continuous-error or speed claim.
`examples/TypedAc.cs` demonstrates typed complex AC amplitudes.

Linux executable names omit `.exe`. Build the managed library before F# examples load it. `scripts/Test-SecondSlice.ps1` runs the current build/tests and typed examples. Source/revision evidence is under `evidence/`; generated logs and binaries stay outside Git.

`scripts/Test-ManagedCoverage.ps1 -CoverageTool <path-to-dotnet-coverage>` measures the client SDK separately from its conformance executable.

Rust coverage uses the pinned `rust-toolchain.toml` toolchain by default. Run
`./scripts/Test-Coverage.ps1 -CheckPrerequisitesOnly` before expensive testing,
then `./scripts/Test-Coverage.ps1` to execute the instrumented suite and measure
owned `src/*.rs` lines, functions and regions. Both LLVM executables must exist
and run from the selected compiler's sysroot; diagnostics print the resolved
toolchain, host, paths and repair command. Windows MSVC also requires Visual C++
Build Tools and the Windows SDK.

For an intentional override, pass `-CoverageToolchain <version-or-dated-nightly>`
or set `SPICE_COVERAGE_TOOLCHAIN` (the parameter takes precedence), and install
`llvm-tools-preview` for that exact toolchain. No floating nightly is selected
automatically. Coverage environment settings are restored on success or failure.

CI runs the existing build, Rust and managed tests, formatting and lint gates
on Linux and Windows, then measures Rust coverage on each. The missing-tool
regression check uses a disposable CI toolchain and restores renamed tools.
Each coverage artifact retains fresh raw profiles, merged profile, full LLVM
export and summary with source hashes and tool versions; the existing
`evidence/rust-coverage.json` output is preserved. A prerequisite check or
passing test suite alone is not a coverage measurement. Branch, managed, vendor,
GPU and native-plugin coverage remain unavailable in this Rust report.

## Documents

- [Architecture](docs/architecture.sdoc), [implementation plan](docs/implementation-plan.sdoc), [backlog](BACKLOG.md).
- [Original bounded profile](docs/prototype-contracts.sdoc), [second-slice requirements](docs/second-slice.sdoc), [extended acceptance contracts](docs/second-slice-contracts.sdoc).
- [Current implementation status](docs/implementation-status.md), [solver adoption experiment](docs/faer-adoption.md).
- [Accuracy-gated ngspice benchmark](docs/benchmarks.md), [bounded managed transport tests](docs/transport-testing.md).
- [Iteration ledger](evidence/iterations.md), checkpoint reports, [mutation results](evidence/mutation.json), [coverage](evidence/coverage.json).

## Limits and remaining work

No GPU execution, native OSDI plugins, Arrow transport, persistent worker sandbox, C ABI, general Starlark runtime, remote service or GUI is implemented. A bounded Shockley diode/Newton path and SForge v0.1 parser/lowering path are implemented, but expression IR is not yet an integrated behavioral device. AC source magnitude/phase is supported for the selected import/API path. No general vendor-model or StressForge compatibility is claimed.

The bounded text/import frontends cover selected R/C/L/V/I, diode, controlled-source, waveform, directive and ground-alias forms. They are not a broad SPICE dialect; unsupported syntax fails explicitly.

The legacy transient path uses zero stored-energy startup and records the first positive step. The configured path starts from a DC operating point unless a full consistent MNA state is supplied, and records t=0. Adaptive tolerance controls local step-doubling estimates, not global error or unobserved waveform peaks.

The initial size/output budgets remain enforced. The local artifact API requires trusted local storage; integrity hashes are not signatures or a sandbox. Campaigns are bounded and sequential, without crash resume or distributed scheduling.

The full plan remains partially implemented. CI is configured but no remote execution is claimed. Product source licensing and distribution qualification remain open decisions.


Generated Rust/C# mutations: see [mutation framework instructions](docs/mutation-frameworks.md).
