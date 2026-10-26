# Continuation checkpoint 344

Campaign steps 1–10 (repository iterations 335–344), based on 1b8885e.
Source parent 8a6b624 plus the checkpoint patch; instrumented source/test
SHA-256 values are retained in continuation-344-rust-coverage.json.

Finite norms, nested options, correction dimensions, explicit-RHS policy rejection,
BE history/excitation arithmetic, thermal trial limiting and complete correction
work accounting now have regression evidence. Before the fix, an empty BE provider
correction reproduced a panic (local 337-red.log, exit 101).

Checkpoint.cs 344 passed build, 257 Rust tests, formatting, Clippy with warnings
denied, managed build/29 checks and six xUnit cases. An initial Clippy failure
for an unnecessary test cast was corrected before the successful full rerun.
Two Pester framework input/sharding contracts passed; malformed workers, filters,
budgets and paths were rejected before tool startup.

Independent physical checks cover six thermal voltages × four initial states,
unequal 2/4/8-diode chains, 72 seeded coupled-chain solves across three CPU providers
and both recovery policies (seed 0x434841494e333431), and 36 bridge solves at both
polarities. Bridge voltage, KCL and power use a monotonic scalar-root oracle.
The tighter 1e-16 A bridge policy still exhausts its source-stage budget; its
counterexample is retained in counterexamples/bridge-tight-current.json. Default
1e-12 A convergence passes the unchanged physical oracle tolerances.

Native cargo-mutants 27.1.0 completed in 306.77 seconds: 33 candidates, 24 caught,
nine survived, zero timeouts/unviable/unattempted. Scope was limited_candidate in
src/newton.rs with continuation_hardening, one worker. Surviving boundary,
ceiling and damping-report changes are retained as unresolved findings; equivalent
status has not been established. Stryker.NET was explicitly skipped in this run.
The supplemental custom numerical driver validated 29 mutation sites without
executing them; that preflight is not mutation detection evidence.

Linux/WSL rustc 1.99.1 instrumentation measured 4,453/4,930 owned source lines
(90.32%), excluding dependencies/tests. Rust branch coverage is unavailable.
Windows Rust instrumentation has not been rerun after SDK installation.
Managed owned-source coverage is 141/151 mapped lines and 52/66 branches;
Checkpoint.cs is 40/41 lines and 21/30 branches, and FrameworkMutations.cs is
78/94 lines and 67/146 branches. See scoped JSON for hashes, raw report paths and
mapping limitations. Helpers used managed file-based execution, not NativeAOT.

All 12 StrictDoc documents parse with 0.30.4. An extra blank line in the new
requirements initially failed parsing and was corrected. REF4-014/015/016 remain
derived Draft requirements; these finite tests do not establish broad closure.
No ngspice oracle was rerun here, no sparse nonlinear assembly qualification,
and no full SPICE/GPU/production claim is made.

Reproduction from a validated checkout with pinned tools imported:

- dotnet run --file scripts/Checkpoint.cs -- 344
- scripts/Test-Coverage.ps1 -CoverageToolchain 1.99.1-x86_64-unknown-linux-gnu (Linux)
- scripts/Run-FrameworkMutations.ps1 -Minutes 18 -Tool rust -RustFile src/newton.rs -RustTest continuation_hardening -RustFilter limited_candidate -Workers 1
- Invoke-Pester -Script tests/FrameworkMutations.Tests.ps1
- Get-Content -LiteralPath examples/continued-bridge.json -Raw | ./target/debug/circuit-simulator.exe

Raw artifacts remain in ignored build/qualification-campaign and
work/framework-mutations/20261026T222312Z-a57323f2. Bounded counts and hashes are
version controlled in checkpoint-344.json and continuation-344-*.json.
