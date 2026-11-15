# Controlled-current source compatibility implementation plan

> **For agentic workers:** Use superpowers:executing-plans to implement this plan task-by-task, with test-first verification and separate mutation outputs.

**Goal:** Execute five unchanged original current-source programs from the pinned reference corpus and agree with stock ngspice on every requested DC point.

**Architecture:** Extend generated ANTLR semantic profiles, preserve source ranges and scoped expressions, and reuse existing affine behavioral lowering and native VCCS stamps. Explicit G multiplicity scales the complete current law; a declared nested parameter M remains an ordinary scoped parameter unless explicitly consumed by the device. No parser/lexer replacement, new numerical model or dependency.

**Tech stack:** Existing C#/.NET10 frontend, ANTLR, Rust engine, PowerShell/F# qualification, FsCheck and StrictDoc.

**Spec:** docs/controlled-current-sources.sdoc.

## Constraints and reference decisions

- Keep original inputs, native defaults and established voltage/current tolerances. No replay substitution or omitted analyses count as a new original pass.
- Keep artifacts/builds/TEMP on E:, source on existing NTFS checkout, and use the owned-tree watchdog for heavy work. Preserve canonical main and unrelated changes.
- Reuse-before-build: RSpice already parses G CUR aliases and resolves source multiplicity. Its flattener explicitly distinguishes Xyce implicit X-line scaling from ngspice formal M parameters. Our independent oracle probes confirm -180V for explicit G M=10, but -18V for an unused declared nested M. Retain that dialect distinction.
- Native engine and public wire already implement affine VCCS/current offsets, including DC/AC/transient; reuse them. General nonlinear current expressions, E VOL aliases, implicit Xyce multiplicity and extra source attributes remain outside this slice.
- A Dafny trial offers little value for this generated-grammar/existing-evaluator adapter; retain generated parser bounds, atomic preparation and executable property tests instead. No new proof claim.

## Review focus

- Reject duplicate/unknown attributes and malformed generated headers atomically.
- Reject zero, negative, nonfinite or overflowing multiplicities; preserve signed finite gains and offsets.
- Evaluate formal M in its lexical instance scope; do not invent implicit scaling.
- Apply multiplicity to offsets and every coefficient, including differential controls.
- Preserve source ranges, node/branch identities, unchanged native default analysis and all observable comparisons.

## Task 1: Generated metadata and affine lowering

Files: compat/ngspice/grammar-implementation.json, SpiceParser.g4, PrimitiveSyntax.cs, BehavioralSourceSyntax.cs, DeclarationExtraction.cs, OperatingPointBridge.cs, BehavioralSourceLowering.cs; tests in ControlledCurrentSourceTests.cs.

- [x] Add failing tests for linear G M, affine G CUR, differential/offset scaling, nested formal M and refusal boundaries.
- [x] Run and retain RED, then add generated semantic profiles and typed metadata.
- [x] Route G CUR through the existing behavioral adapter; scale validated affine output using the existing evaluator and VCCS/current stamps.
- [x] Run focused regression/negative/property checks and retain seeds.

## Task 2: Real requested execution and qualification

- [x] Rebuild managed runtime on E:; run the unchanged five original DC programs on LU and Faer against fresh stock ngspice output.
- [x] Exercise an authored OP/AC/transient circuit through the public client; keep it separate from original count.
- [x] Run relevant/full managed checks, physical Intel routine/probe, real StrictDoc parsing, actual scoped line/branch coverage and isolated mutation checks; report skips and limits.
- [x] Review, record bounded evidence and updated burndown, commit verified implementation and push the active development branch (`d43d88b`).
