# Thirty original blocked decks: selection and implementation plan

> **For agentic workers:** Use superpowers:executing-plans to implement this plan in the existing isolated development worktree. Do not start parallel implementation or delegate without authorization.

**Goal:** Execute and independently qualify thirty additional unmodified originals, solving shared causes rather than special-casing file names.

**Architecture:** Keep ANTLR syntax/declarations, immutable .NET preparation and typed requests separate from Rust expression/model evaluation and backend adapters. Extend existing interfaces end to end. No new handwritten parser, embedded legacy simulator, placeholder model or hidden CPU fallback.

**Tech Stack:** Existing pinned .NET/Rust, generated ANTLR grammar, PowerShell/C# orchestration, StrictDoc, established property/mutation/coverage tooling.

**Spec:** architecture.sdoc, requested-timeint.sdoc, prototype-contracts.sdoc and corpus-cohort-qualification.sdoc. The exact frozen cohort is ../compat/ngspice/thirty-decks-inventory.json.

## Baseline and scope

Source checkout d27635c162f7edc0e3746c690d4cd2c085f68206; retained corpus 29c28a022625fd7cc2c56ead1cce2d3066663074. All thirty original files pass structural parsing and fail requested-analysis preparation. RequestedCorpus completed in 16.9401797 seconds with valid source/artifact provenance. It did not execute numerical jobs or reference comparisons for refused preparations. See ../evidence/thirty-decks-baseline.json and ../evidence/thirty-decks-burndown.svg. This is a selection baseline, not thirty implemented decks.

The baseline executes the preceding checkpoint's frozen runtime-reviewed assemblies and engine-final binary, not a fresh rebuild of publication-normalized sources. Explicit executable/DLL hashes identify the tested bytes. Preserve that distinction when comparing later builds.

All bulk output remains E:/Artifacts/Spice-thirty-20261116T014236Z. The source stays J:/dev/spice-deck-next on fix/deck-blocker-implementation. Preserve canonical main, unknown sheet-resistor changes, existing raw artifacts and active endurance jobs. No CI, main merge or soak belongs to this plan. Ordinary host RAM is managed by Windows; only observed runaway mutant growth permits targeted intervention. Keep bounded concurrency, cancellation and disk checks.

## Priorities and independently testable slices

1. **Math laws, eight originals:** atan_tan, int_floor_ceil_bsrc, abmpow3, sct, sqrt, binary_funcs, unary_funcs and ternary_op. Extend the existing generated expression compiler, typed scalar wire tree and native analytic derivatives. Start with smooth trigonometric functions; separately specify singular domains, zero arguments, discontinuities and conditional short-circuiting. Do not assign fictitious finite derivatives to undefined points to make a sample pass.
2. **Controls and time, eight originals:** func, time, bug1085son and its reference fixture, bug_1806_2/2a, solDepRes2 and issue310. Multiple differential controls need explicit typed dependencies and MNA Jacobian columns; do not squeeze them into one fictitious input or replace missing controls with zero. Time must be evaluated at actual trial times with physical breakpoint handling; TABLE and modulo must retain authored semantics. Keep startup graph admission distinct from full nonlinear convergence.
3. **Passive models, fourteen originals:** cmod_capmod, lmod_indmod; TEMPCAP's three examples; TEMPIND's three examples; semicap and semicap_subc; inductor_ic; CAPACITOR/capacitor; BUG_374/capacitor and capacitor-bug. Distinguish instance/model precedence, multiplicity, geometry-derived capacitance, TC1/TC2/TNOM and initial conditions. Some originals use .tran 0; diagnose that dialect behavior explicitly rather than rewriting their input or inventing a step.

Related fixtures are retained as separate original files; thirty files do not imply thirty model families. Exclude both preceding twenty-file inventories and reject duplicate input identities. Current preparation refusal independently establishes that every selected file is presently blocked. Preserve further discovered blockers instead of silently swapping in an easier already-passing deck.

## Implementation and verification map

### Slice A: Typed smooth and piecewise scalar operators

Files: dotnet/Simulator.Syntax/SingleControlVoltageCompiler.cs; dotnet/Simulator.Client/BehavioralExpression.cs; dotnet/Simulator.Frontend/BehavioralSourceLowering.cs; src/expression.rs; affected generated request schema; tests/expression_operators.rs and managed BehavioralVoltageTests.

- [ ] Add failing native value/analytic derivative tests against independent equations and central differences away from singularities, plus malformed wire/depth/domain failures.
- [ ] Add failing generated-parser-to-wire tests for each supported function/operator and verify refusal publishes no partial plan.
- [ ] Implement only the explicitly specified operator slice through all consumers; retain node/depth bounds, finite checks and exact wire discriminators.
- [ ] Run relevant property/fuzz and selected mutation checks, measure scoped coverage, re-run affected originals on LU/Faer and actual Intel where supported, and commit each verified slice.

### Slice B: Multiple controls and time-dependent laws

Files: generated compiler and immutable declarations above; typed client expressions; native compile.rs/expression.rs/configured.rs and audit consumers; requested-analysis preparation. Create focused behavioral dependency/time tests rather than extending one monolithic harness.

- [ ] Trace producers, hierarchy binding, control-node identities, MNA stamps, startup, accepted/trial time, rollback and endpoint/KCL audits before changing the wire contract.
- [ ] Write failing reversed-polarity, unused function argument, multi-control derivative, time-corner and rejected-trial tests. Refuse incomplete/unsupported combinations atomically.
- [ ] Implement explicit immutable control/time bindings; reuse existing interpolation and waveform facilities where their endpoint and breakpoint semantics actually fit.
- [ ] Qualify each newly executable original against all requested ngspice analyses; check RSpice source for divergent behavior and commit verified changes.

### Slice C: Capacitor/inductor model and IC semantics

Files: existing passive declaration/preparation classes, requested initial-condition preparation, typed RLC model bindings and native dynamic charge/flux consumers as needed; dedicated managed/native passive-model tests.

- [ ] Pin geometry, units, defaults, instance/model precedence and temperature conventions with analytic and negative tests before broadening admission.
- [ ] Compare original model behavior with stock ngspice and inspect the retained RSpice implementation; distinguish native dialect refusal from numerical disagreement.
- [ ] Implement complete charge/flux and startup behavior for each admitted subset; preserve cancellation/rollback and input-domain failures.
- [ ] Run affected originals with every authored analysis and observable; qualify CPU/GPU backends separately and commit each verified family.

## Reuse and numerical acceptance

Retain the adopted Rust/native core and ANTLR/.NET boundary. Reuse Rust/.NET standard mathematical functions and existing waveform interpolation; custom work is the typed circuit semantics, derivatives and integration glue. Inspect pinned RSpice as behavioral guidance, and keep ngspice an external independent oracle. This plan does not add a dependency or supersede the documented SpiceSharp/ngspice reuse evaluations. Revisit reuse before any substantial new interpolation engine or generalized multi-input expression subsystem.

For every slice: preserve original input/dependency hashes, all requested analyses and complete signal maps; use existing unweakened comparison bounds. Classify executed-but-different, native-reference failure, unsupported, cancelled and unattempted separately. A refined reference is diagnostic, not stock-default agreement. Update StrictDoc with stable IDs and real evidence; Draft remains Draft until its acceptance scope is met.

Timing uses scripts/Measure-OriginalDeckTimings.ps1 and evaluation/RSpiceTimingProbe.rs only after correctness qualification. Record one warmup, at least three repetitions, executable/library hashes, build profiles and actual wall-time distributions. Ours' prepared-request timing excludes SPICE preparation; cached RSpice core is debug/unoptimized. Report these differences and batch/DC process overhead. Obtain a release RSpice core before claiming release-to-release speed superiority. Failed work supplies no speedup.

## Review focus

- Undefined derivatives, lazy conditional domains and discontinuous round/step functions: tests belong to Slice A.
- Reversed or repeated control nodes, hierarchical names and unused parameters: tests belong to Slice B.
- Time evaluated at stale accepted state after a rejected step: rollback/corner tests belong to Slice B.
- Temperature and geometry units, IC/UIC/operating-point precedence and multiplicity: tests belong to Slice C.
- Missing reference vectors, omitted analyses, altered inputs and debug/release timing mismatch: cohort qualification must reject inflated success/performance counts.

## Completion gate

- [x] Freeze thirty unique currently blocked originals and record input hashes and first blockers.
- [x] Run the actual baseline coordinator with immutable artifact/source provenance and publish bounded burndown data.
- [ ] Implement shared blockers, with verified family commits and relevant negative/property/mutation/coverage checks.
- [ ] Execute and compare all requested analyses against stock ngspice and RSpice; retain honest unresolved outcomes.
- [ ] Collect scoped timings, perform final source review, parse StrictDoc, update one results issue per actual run and push verified development checkpoints. Do not merge canonical main implicitly.
