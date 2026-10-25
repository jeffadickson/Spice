# Spice repository handoff prompt

You are continuing work on **Spice**, a Rust circuit-simulation repository. Work from the repository root and preserve its user defaults:

- Use PowerShell for shell/file work and C# or F# for substantive scripts.
- Keep StrictDoc authoritative. Add stable requirement IDs and source/test evidence for each implemented capability.
- Document public interfaces and numerical conventions. Run meaningful malformed-input, randomized/property, mutation, and coverage checks; report real limitations.
- Commit locally in small numbered iterations. Build at the stated checkpoint cadence. Do not claim GPU, plugin, Arrow, full SPICE compatibility, or production qualification without evidence.

Current implementation through iteration 304:

- Bounded dense MNA engine for R/C/L, independent sources, DC/AC/linear transient.
- Independent faer sparse and nalgebra dense CPU paths.
- C#/F# JSON clients, artifacts, variation grids and deterministic sampling.
- New bounded Shockley diode DC operating-point path: analytic residual/Jacobian, damped Newton solve, explicit options, convergence report, diode netlist and C# DTO support.
- Fixed-step nonlinear backward-Euler transient diode stepping is available with analytic Newton residuals/Jacobians and rollback-safe committed history.
- Nonlinear AC bias linearization and configured/adaptive nonlinear transient stepping are intentionally unsupported.

Start by reading `README.md`, `docs/implementation-status.md`, `docs/fourth-slice.sdoc`, `evidence/implementation-validation-150.json`, the latest `evidence/checkpoint-*.json`, and `evidence/iterations.md`.

For authoring evolution, read `docs/modern-dsl-proposal.sdoc` before designing a parser. SForge is a proposal only: keep the current JSON protocol stable until an AST/IR and migration contract are implemented.

Suggested next technical work:

1. Add DC-bias small-signal AC linearization using diode incremental conductance.
2. Extend nonlinear transient MNA to configured/adaptive stepping, source breakpoints, and independent reference fixtures.
3. Add continuation/source stepping for difficult DC operating points.
4. Expand device models only after equation, derivative, domain, unit, and convergence contracts are written.

Run `dotnet run --file scripts/Checkpoint.cs -- <multiple-of-10>` for checkpoints. Re-parse all `.sdoc` files after requirement changes. `scripts/Test-Coverage.ps1` now selects a nightly MSVC coverage toolchain containing `profiler_builtins`; this host still needs Visual C++ Build Tools/Windows SDK for `link.exe` before Rust coverage can run.
