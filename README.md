# Rust Circuit Simulator — reference implementation

This repository contains the architecture, a 23-package implementation plan, and the first **bounded linear-circuit reference engine**. It is not a qualified M1 simulator.

## Working features

- Rust core with immutable compiled plans and a per-engine backend registry.
- Reused nalgebra dense LU and QR; LU supports complex AC. These are two methods in one library, **not two independently qualified solver providers**.
- Resistors, capacitors, independent constant voltage/current sources; DC, linear AC and fixed-step backward-Euler transient from zero stored-energy state.
- Strict bounded JSON requests and success/failure envelopes with signal units, complex components and request hashes.
- Small R/C/V/I text parser, suffixes, interpolation/assertion helpers and an experimental differentiable expression tree.
- C# asynchronous process client, F# access and a trusted F# circuit builder.
- Analytic, negative, seeded property/fuzz-smoke and deliberate mutation checks; real coverage data or explicit unsupported-toolchain records.

## Run

Install the toolchain from `rust-toolchain.toml` and .NET SDK from `global.json`, then execute from this directory:

```powershell
./scripts/Test.ps1
Get-Content -Raw examples/divider.json | ./target/debug/circuit-simulator.exe
dotnet run --file examples/Run.cs -- target/debug/circuit-simulator.exe examples/rc.json
dotnet fsi examples/Run.fsx target/debug/circuit-simulator.exe examples/divider.json
dotnet fsi examples/BuildCircuit.fsx target/debug/circuit-simulator.exe
dotnet run --file scripts/Mutation.cs
```

Managed coverage can be reproduced with `scripts/Test-ManagedCoverage.ps1 -CoverageTool <path-to-dotnet-coverage>` after installing the .NET coverage tool in a tooling directory. Its SDK metrics exclude the conformance executable; they do not measure Rust.

Linux executable names omit `.exe`. The C# library must be built before F# examples load it. Use `scripts/Test-Coverage.ps1` on a Rust target that includes the profiler runtime. The tested Windows GNU toolchain does not; this limitation is recorded explicitly. The Linux CI coverage job is configured but has not been executed remotely.

## Authoritative documents and evidence

- [Architecture](docs/architecture.md) and [StrictDoc source](docs/architecture.sdoc).
- [Implementation plan](docs/implementation-plan.md) and [StrictDoc source](docs/implementation-plan.sdoc).
- [Backlog](BACKLOG.md) and [machine inventory](backlog.json).
- [Prototype acceptance contracts](docs/prototype-contracts.sdoc).
- [Development standards](docs/development-standards.sdoc).
- [Iteration ledger](evidence/iterations.md), checkpoint reports, [mutation outcomes](evidence/mutation.json) and [coverage scope/results](evidence/coverage.json).

The 50 implementation passes are individually committed. Builds are checked at 10/20/30/40/50; qualification claims follow evidence, not iteration count. Most planned work packages remain Proposed or partially explored.

## Deliberate limitations

No sparse/native CPU backend, GPU execution, nonlinear device/Newton engine, inductors, adaptive timesteps, event kernel, OSDI plugins, Arrow results, persistent worker sandbox, C ABI, Starlark interpreter, remote service or GUI is implemented. The expression tree is not yet an integrated behavioral device. No universal vendor-model or StressForge compatibility is claimed.

The text subset allows a single-token first-line title or explicit `title:` line, R/C/V/I four-token devices, `.op`, `.end` and whole-line `*` comments. It rejects includes, control commands and unsupported devices. This is not general SPICE3/dialect compatibility.

Dense reference limits: at most 128 nodes, 1024 elements, 256 MNA unknowns; transient at most 100,000 steps and 2,000,000 retained scalars; AC at most 10,000 frequencies and 1,000,000 complex scalars. Retained JSON is bounded, not streamed; long-running production workflows require the planned streaming layer.

No remote is configured. The source-license decision is still pending; third-party numerical/model licenses and packaging require review before distribution.

