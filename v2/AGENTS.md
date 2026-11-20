# Spice V2 Engineering Instructions

This branch (`v2`) is a clean C++20 + .NET reset of the circuit simulator following the Xyce-style DAE architecture and Strangler Fig transition plan. Legacy v1 Rust/ANTLR code stays untouched on `main` and `fix/deck-blocker-implementation`.

## Core Architecture Rules

- **Core engine**: Lean C++20 (`v2/cpp/`), MIT/Apache-2.0, zero global mutable circuit state, no Trilinos. Build with CMake + `vcpkg` (`v2/vcpkg.json`).
- **Linear solvers**: SuiteSparse KLU (LGPL-2.1+ shared library) as primary sparse direct solver plus a small built-in dense reference solver. Keep solver adapters behind circuit-independent CSC/linear contracts.
- **Universal DAE & models**: Every device implements one mathematical contract ($F(x,t) + dQ(x,t)/dt = 0$, local derivatives, noise PSDs, trial/commit rollback). Compact semiconductor models use one generic OSDI 0.3/0.4 loader rather than ad-hoc per-family FFI hacks.
- **Strangler Fig `XyceBackend`**: External process adapter only (GPL-isolated, zero Xyce/Trilinos types in core IR or API). Provides Day-1 execution and differential oracle verification while `NativeCppBackend` replaces it slice-by-slice.
- **Public boundaries**: Expose a flat `extern "C"` ABI (`v2/include/spice/capi.h`) with opaque handles, version checks, and exception containment. C#/.NET SDK (`v2/dotnet/`), `StarLark-cs` DSL, and schematic UI consume only the Canonical IR and public API — never internal solver or model state.
- **Unified Device Descriptor**: Schematic symbols, properties, ERC, IR emission, netlisting, and waveform probing share one fail-closed descriptor. Unknown devices, levels, or parameters fail explicitly.
- **DSL boundary**: `StarLark-cs` (and future frontends like Cameo) emit immutable Canonical IR before simulation; no definition interpreter runs inside the Newton/device hot loop.
- **No legacy shell in core**: Do not build an ngspice `.control`/Nutmeg interpreter in the core engine. Dialect translation stays isolated from core solver semantics.
- **GPU discipline (`DEV-GPU-001`)**: Do not add or gate on GPU solver work until the CPU C++20 + KLU engine is qualified and benchmarked faster than ngspice on 1,000–10,000+ node circuits.

## Authoritative Requirements (StrictDoc — 6 Files Only)

Keep all authoritative V2 requirements in the six StrictDoc files under `v2/docs/requirements/`:
1. `architecture.sdoc`
2. `capabilities.sdoc`
3. `schematic.sdoc`
4. `physical-audits.sdoc`
5. `implementation-plan.sdoc`
6. `development-standards.sdoc`

- Preserve stable UIDs (`ARCH-*`, `AUDIT-*`, `PLAN-V2-*`, `DEV-*`) and update `v2/backlog.json` when work-package metadata changes.
- **Anti-sprawl rule**: Do **not** create one-off `.sdoc` files per bugfix, PR, or mutant replay. Update the relevant authoritative domain document instead.
- Mark statuses honestly (`Proposed`, `Accepted`, `Draft`, `Implemented`, `Verified`). Never claim unexecuted tests, benchmarks, or coverage passed.

## Tiered Verification (Fast Velocity, Honest Rigor)

- **Tier 1 (every commit, target <60 s)**: C++20 unit tests (`ctest`), malformed/negative boundary tests, opt-in independent physical audits, and fast differential smoke tests.
- **Tier 2 (PR / milestone)**: Curated `NativeCppBackend` vs. `XyceBackend` / ngspice differential oracle suite, property tests, and actual line/branch coverage (`gcov`/`llvm-cov` for C++, Coverlet for C#) with explicit scopes and limitations.
- **Tier 3 (scheduled / release gate)**: Targeted mutation testing on numerical/audit kernels and bounded parser/ABI fuzzing. Never block feature velocity on multi-day full-repo mutation epochs.

## General Hygiene

- Comment public APIs, units, sign conventions, ownership, and non-obvious numerical logic meaningfully; keep comments synchronized with code.
- Use C#/F# for automation scripts by default and PowerShell cmdlets on Windows (or standard shell tools in Linux CI where PowerShell is absent).
- Never commit build directories (`v2/build/`), downloaded binaries, credentials, or large corpora.
