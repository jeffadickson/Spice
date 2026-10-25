# Rust Circuit Simulator — extended reference engine

A bounded Rust circuit engine with CPU provider adapters and C#/F# access. This is **not a full M1 release**.

## Working scope

- Independent numerical libraries: nalgebra dense LU/QR and faer sparse LU (real and complex). The assembler still uses dense matrices and a 256-unknown cap; no large-circuit scaling claim is made.
- Immutable circuit plans, explicit capabilities, prepared numeric-factor reuse and independent residual checks.
- R/C/L and independent sources; DC, complex AC, legacy fixed-step transient, and configured backward-Euler/trapezoidal transient.
- A bounded Shockley diode DC operating-point path with analytic damped Newton iterations and convergence evidence. Nonlinear AC and transient analyses currently return explicit unsupported errors.
- Configured consistent initial states, PWL/sine source overrides, source corners, optional adaptive step doubling, accepted-sample observers and integration statistics.
- Unit-aware measurement/assertion results, DC power diagnostics and bounded sequential campaigns with separate case outcomes.
- Immutable scalar sweeps and bounded Cartesian grids, including reproducible seeded uniform and log-uniform value plans with unit-aware per-point results.
- One-shot JSON CLI, capability discovery, typed C# requests/results, and F# builders.
- Trusted local result artifact publication and SHA-256/byte-count verification.
- Analytic, malformed-input, seeded property/fuzz-smoke and selected mutation tests with actual coverage scope/limitations.

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

Linux executable names omit `.exe`. Build the managed library before F# examples load it. `scripts/Test-SecondSlice.ps1` runs the current build/tests and typed examples. Source/revision evidence is under `evidence/`; generated logs and binaries stay outside Git.

`scripts/Test-ManagedCoverage.ps1 -CoverageTool <path-to-dotnet-coverage>` measures the client SDK separately from its conformance executable. `scripts/Test-Coverage.ps1` requires a supported Rust profiler runtime. The tested Windows GNU distribution lacks it; unavailable Rust coverage is explicitly recorded, never inferred from managed metrics.

## Documents

- [Architecture](docs/architecture.sdoc), [implementation plan](docs/implementation-plan.sdoc), [backlog](BACKLOG.md).
- [Original bounded profile](docs/prototype-contracts.sdoc), [second-slice requirements](docs/second-slice.sdoc), [extended acceptance contracts](docs/second-slice-contracts.sdoc).
- [Current implementation status](docs/implementation-status.md), [solver adoption experiment](docs/faer-adoption.md).
- [Iteration ledger](evidence/iterations.md), checkpoint reports, [mutation results](evidence/mutation.json), [coverage](evidence/coverage.json).

## Limits and remaining work

No GPU execution, native OSDI plugins, nonlinear semiconductor/Newton engine, Arrow transport, persistent worker sandbox, C ABI, Starlark runtime, remote service or GUI is implemented. Expression IR is not yet an integrated behavioral device. Source DC values are reused as real AC amplitudes; separate AC phase/amplitude settings are deferred. No general vendor-model or StressForge compatibility is claimed.

The simple text frontend supports R/C/L/V/I, `.op`, `.end`, whole-line comments and a single-token or explicit `title:` first line. It is not a broad SPICE dialect.

The legacy transient path uses zero stored-energy startup and records the first positive step. The configured path starts from a DC operating point unless a full consistent MNA state is supplied, and records t=0. Adaptive tolerance controls local step-doubling estimates, not global error or unobserved waveform peaks.

The initial size/output budgets remain enforced. The local artifact API requires trusted local storage; integrity hashes are not signatures or a sandbox. Campaigns are bounded and sequential, without crash resume or distributed scheduling.

The full plan remains partially implemented. CI is configured but no remote execution is claimed. No Git remote is configured. Product source licensing and distribution qualification remain open decisions.

