# Rust Circuit Simulator

Implementation planning baseline — October 24, 2026.

A new circuit simulation engine with a Rust-owned core, reusable CPU/GPU numerical backends, model plugins, a versioned API, and first-class C#/F# access. C# is the intended GUI language if a GUI is needed. Starlark is a candidate definition frontend; its selection remains an M0 experiment.

This repository currently contains **architecture, requirements and an implementation plan**. It contains no implemented simulator, validated device models or measured performance claims.

## Authoritative documents

- [Implementation plan](docs/implementation-plan.md) ([StrictDoc source](docs/implementation-plan.sdoc)): work packages, dependencies, milestone gates, evidence and the first execution sequence.
- [Architecture](docs/architecture.md) ([StrictDoc source](docs/architecture.sdoc)): boundaries, numerical contracts, solver portfolio, plugin trust, .NET API and DSL evaluation.
- [Development standards](docs/development-standards.md) ([StrictDoc source](docs/development-standards.sdoc)): C#/F# scripts, PowerShell, comments, StrictDoc, coverage and adversarial/random testing.
- [Backlog overview](BACKLOG.md): a compact dependency and work-package table.
- [Backlog](backlog.json): machine-readable work-package inventory derived from the implementation plan.
- [Validation report](evidence/planning-validation.json): document integrity and traceability checks for this planning baseline.

Earlier exploratory drafts outside this repository are historical. Edit these repository documents going forward. Source-of-truth requirements and dependencies live in SDoc; regenerate `backlog.json` after editing work packages.

## First implementation sequence

1. Establish pinned toolchains, CI and coverage/test reporting.
2. Freeze equation, matrix and request/result contracts against small fixtures.
3. Compare native solver integration and Rust/C++ adapter costs.
4. Qualify a CPU sparse backend and basic device assembly.
5. Deliver operating point, AC and transient through a C#/F# client.
6. Qualify a second provider before claiming backend interchangeability.
7. Add compiled-model isolation, then qualified GPU execution and the selected definition frontend.

The full work-package dependency graph and exit criteria are in StrictDoc. Do not scaffold dozens of empty crates or implement every solver adapter before the numerical and managed-client vertical slices work.

## Tools and verification

Rust/C++ is subject to an evidence-based M0 decision; the current architecture recommends Rust with narrow native adapters. Use C# file-based applications or F# Interactive for substantive scripts and PowerShell for shell/file operations. Python is only a documentation-tool runtime exception where StrictDoc requires it.

With StrictDoc 0.30.4 installed in a tooling environment:

```powershell
strictdoc export docs --output-dir build/docs
```

The planning baseline has been checked with the StrictDoc parser. Parser success is not an HTML rendering or simulator qualification claim. Runtime coverage and numerical/GPU test reports are unavailable because implementation has not started.

## Repository policy

Local Git only; no remote is configured. Product/source licensing is an explicit M0 decision. Third-party solvers, model packages and compiler runtimes require component-level license and distribution review before adoption.
