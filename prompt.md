# Spice repository handoff prompt

You are continuing work on **Spice**, a Rust circuit-simulation repository. Work from the repository root and preserve its user defaults:

- Use PowerShell for shell/file work and C# or F# for substantive scripts.
- Keep StrictDoc authoritative. Add stable requirement IDs and source/test evidence for each implemented capability.
- Document public interfaces and numerical conventions. Run meaningful malformed-input, randomized/property, mutation, and coverage checks; report real limitations.
- Commit locally in small numbered iterations. Build at the stated checkpoint cadence. Do not claim GPU, plugin, Arrow, full SPICE compatibility, or production qualification without evidence.

Current implementation through the M1 qualification tranche extensions (remote
`main` numerical core at checkpoint 320, qualification tooling through iteration 333):

- Bounded dense MNA engine for R/C/L, independent sources, DC/AC/linear transient.
- Independent faer sparse and nalgebra dense CPU paths.
- C#/F# JSON clients, artifacts, variation grids and deterministic sampling.
- Bounded Shockley diode DC operating-point path: validated SI parameters, analytic residual/Jacobian, finite-difference derivative corpus, damped Newton solve, explicit options, convergence report, diode netlist and C# DTO support.
- Diode AC bias linearization plus fixed-step and configured backward-Euler nonlinear diode stepping are available with analytic Newton residuals/Jacobians and rollback-safe committed history. Other nonlinear integration methods fail explicitly.
- ngspice deck lowering covers selected directives, continuation lines, ground aliases, bounded subcircuits/parameters/expressions, `.model D`, `.temp`, and explicit bounded options.
- Source syntax includes compact and whitespace waveform forms for PULSE, SIN, PWL, and EXP. AC source magnitude/phase and R/C/L AC values are represented.
- Controlled source support includes VCVS, VCCS, CCCS, and CCVS with dedicated compatibility tests.
- Configured nonlinear backward-Euler supports bounded adaptive/source-override stepping and source breakpoints. DC source continuation, explicit fixed Gmin, and bounded staged Gmin continuation are available. Gmin continuation records every regularized warm-start stage and always finishes with an unregularized physical solve; it is mutually exclusive with source continuation.
- Compiler-owned canonical CSC stamps are consumed directly by faer for linear DC. Dense matrices remain bounded reference/fallback representations; sparse AC/transient and large-circuit performance are not claimed.

The 311-320 slice adds independent DC/legacy-BE physical audits, bounded topology diagnostics, typed audit manifests, explicit refinement comparisons and structured stderr tracing. Read docs/consistency.sdoc and evidence/consistency-qualification.md for scope and evidence; AC/configured-transient physical auditing and global accuracy remain explicitly not_checked.

Start by reading `README.md`, `docs/implementation-status.md`, `docs/ngspice-compatibility.sdoc`, `docs/fourth-slice.sdoc`, `docs/campaign-2-to-5.html`, `compat/ngspice/selected-fixtures.json`, `evidence/mutation.json`, and `evidence/iterations.md`.

Current evidence: checkpoint 320 passed 242 Rust tests, 28 managed checks, formatting and Clippy with warnings denied; all 12 StrictDoc documents parsed. The selected-fixture manifest and named external oracles retain their prior qualification evidence; this slice did not rerun ngspice. Independent ngspice checks pass for `res_simple` current and `res_partition` AC voltage at 1e-12; `lowpass` AC passes at 5e-8 because ngspice's `.print` output rounds its complex components. The 22/22 selected mutations were killed; checkpoint-320.json and evidence/mutation.json retain the exact counts and source hashes; see evidence/continuation-qualification.md for the added Gmin operators and physical oracles. Fuzz-smoke, malformed-input, and randomized/property tests are included in the Rust suite. Managed client coverage was rerun: 140/151 owned lines and 51/66 mapped branches; see evidence/managed-coverage.json. Historical checkpoint-320 Windows coverage was blocked by MSVC tooling. Build Tools/SDK installation and a native compile/link/run probe have since succeeded; Windows Rust instrumentation still needs measurement.

For authoring evolution, read `docs/modern-dsl-proposal.sdoc` before designing a parser. SForge is a proposal only: keep the current JSON protocol stable until an AST/IR and migration contract are implemented.

An overnight campaign is now configured for midnight local time (five-hour maximum) with a 05:05 review. Use make endurance or scripts/Run-Endurance.ps1. Read docs/endurance.md and evidence/endurance-qualification.md; no completed five-hour run is claimed. Campaigns test immutable source archives and keep generated artifacts in ignored work/target paths.

Suggested next technical work:

1. Extend difficult-circuit evidence beyond the current-driven diode recovery, impossible physical reverse bias, and bounded stage-exhaustion cases in tests/continuation.rs; see evidence/continuation-qualification.md. Do not infer broad convergence guarantees from this small topology.
2. Extend direct sparse use to AC/transient only after real/complex dynamic assembly, factor lifecycle, and conformance evidence exist.
3. Grow the selected ngspice corpus cautiously, adding upstream hashes, copied attribution, parser/lowering tests, and named oracle comparisons before claiming support.
4. Expand device models only after equation, derivative, domain, unit, limiting/correction, and convergence contracts are written.

Run `dotnet run --file scripts/Checkpoint.cs -- <positive-checkpoint-iteration; current campaign 344,354,...434>` for checkpoints. Re-parse all `.sdoc` files after requirement changes. Run `dotnet run scripts/Validate-NgspiceFixtureManifest.cs -- compat/ngspice/selected-fixtures.json` after corpus changes and both `scripts/Compare-Ngspice*Oracle.cs` scripts when their fixtures change. `scripts/Test-Coverage.ps1` now selects a nightly MSVC coverage toolchain containing `profiler_builtins`; Build Tools and the Windows SDK are now installed; rerun instrumentation before reporting Windows Rust coverage.

Iterations 323–333 add portable setup, bounded result-branch publication, checkpoint sharing retries and pinned cargo-mutants/Stryker.NET integration. Read docs/remote-runs.md, docs/mutation-frameworks.md, docs/consistency.sdoc (MUT-001/MUT-002) and their evidence reports. Linux/WSL2 Rust line coverage is measured at 4,396/4,887 (89.95%); Rust branch coverage is unavailable. Visual Studio Build Tools/SDK installation has since succeeded and a native compile/link/run probe passed; Windows instrumented Rust coverage still needs to be rerun before a percentage is claimed.

The requested one-hour run on e85c1e2 finished at 3,599.04 seconds, 50,987,008 seeded cases, 22/22 selected mutants detected; it predates framework integration. Its remote-runs/windows-x64-hour-20261026/20261026T163309Z-fd18724d-e85c1e2eff7f branch is published. Earlier partial failures remain preserved. Never relabel that hour as cargo-mutants/Stryker evidence.

Generated mutation tooling uses cargo-mutants 27.1.0 and Stryker.NET 5.0.0. Nightly/remote endurance runs now run the 22 selected checks, reserve half the remaining budget for frameworks, then fuzz/property batches. Framework counts remain separate: survivors, timeouts, invalid, uncovered, ignored and unattempted mutants are not test-detected kills. Short framework runs and measured coverage scopes are in evidence/framework-mutation-qualification.md; no completed five-hour campaign, exhaustive source mutation, full SPICE/GPU or production qualification is claimed.

Current campaign: read docs/qualification-campaign.md and evidence/continuation-344-qualification.md. Iterations 335–344 complete; resume at 345. Preserve the tight bridge counterexample and nine generated mutation survivors. Continue authorized ten-step checkpoint pushes through 434 and final review/fixes 435 under the documented 100+1 interpretation.

Checkpoint 354: continuation steps 335–354 complete; resume at 355. Read evidence/continuation-354-qualification.md. Linux coverage percentages are provisional because LLVM reported 62 function mapping mismatches in both recent exports. Cargo-mutants run 20261026T223639Z-47d7e857 remains active; review its real counts when complete. Do not overwrite or relabel prior evidence.
