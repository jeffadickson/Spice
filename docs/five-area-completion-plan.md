# Five-area completion implementation plan

Goal: deliver the five user-approved Spice qualification improvements, committing
once each has its acceptance evidence. Base: 4eb2644. Execute inline in the requested
main checkout; preserve unknown changes. Verification copies remain isolated.
No push or CI dispatch is authorized by this request.

## Scope and acceptance

1. Transport: bound every managed engine/fixture test, preserve cancellation on
   synchronously completing IO/tasks, independently clean acknowledged workers,
   and test failure before the parent acknowledges its descendant. Replay the
   three original hanging mutants with native Stryker; they must be test-reported
   kills rather than framework timeouts. Other equivalents/unviable cases remain
   separate. Files: Client.cs, managed test/conformance helpers, transport fixture,
   stryker-config.json; StrictDoc QUAL-TRANSPORT-002.
2. Comparison benchmark: reproducible C# external-process runner for paired
   supported OP, AC and configured-transient decks/requests, LU/faer vs ngspice.
   Accuracy gate named outputs before accepting timings; retain warmups/repetitions,
   process-inclusive timing, nullable peak working-set measurements, versions,
   hashes, policies and exclusions. Bounded/negative parser/process self-tests and
   an actual run of each paired case are required. Files: dotnet/Simulator.Benchmarks,
   compat/benchmarks, scripts/Run-ComparisonBenchmark.ps1, docs/benchmarks.md;
   StrictDoc BENCH-001. Reuse System.Diagnostics/System.Text.Json, existing typed
   client DTOs and real ngspice rather than inventing a numerical reference.
3. Difficult circuits: seeded, coupled antiparallel diode ladders over signs,
   source strengths, bad-but-valid starts and source/Gmin policies on LU/QR/faer.
   Compare with an independent nested monotone bisection oracle and physical
   KCL/source/power checks; demand final unregularized solve. Add a named paired
   ngspice DC case and execute it. Fix any demonstrated solver defect, retaining
   minimized counterexamples. Files: tests/difficult_continuation.rs, compat/ngspice,
   benchmark case inventory; StrictDoc QUAL-CONT-002.
4. Sparse AC/transient: generated RC/RLC ladders, non-unit complex drives and
   varied scales/frequencies/steps; independent analytical or dense references,
   residual checks, singular/faulty-provider rejection, direct sparse dispatch and
   prepared-real-factor reuse/invalidation checks. Existing complex direct solves
   are qualified; new prepared-complex caching is outside this bounded slice.
   Files: tests/sparse_qualification.rs and demonstrated core fixes only;
   StrictDoc QUAL-SPARSE-003.
5. Consistency: automatic element-equation AC KCL/source/complex-power audits for
   linear supported R/C/L and controlled sources, using peak phasors S=V*conj(I)/2.
   Configured fixed-step BE energy includes numerical dissipation; linear
   trapezoidal energy uses midpoint voltage/current power. Source overrides use
   endpoint values. Nonlinear BE constant-charge diodes remain supported through
   the existing element equations. Adaptive endpoints do not expose accepted
   half-step history and nonlinear AC samples do not expose bias; record those
   profiles NotChecked, never pass. Add tampered-state, malformed, units, generated
   and real-engine checks. Files: src/audit_ac.rs, src/audit_configured.rs,
   src/audit.rs, src/api.rs, src/lib.rs, tests/analysis_audits.rs;
   StrictDoc AUD-AC-001 and AUD-CONFIG-001.

These are complete bounded slices, not the entire Proposed M1 work packages.
Global waveform accuracy, arbitrary model/dialect compatibility, GPU and production
containment are not acceptance claims.

## Verification contract per task

- Write/execute a discriminating regression before its implementation; preserve
  original failure or mutation evidence and exact seeded inputs.
- Run relevant complete Rust/managed suites and analyzers, malformed/property and
  native mutation checks; collect actual scoped line/branch coverage where available.
- Use real StrictDoc parsing and Sphinx warnings-as-errors; maintain examples and
  source/tool/run provenance. Raw artifacts stay in ignored work/build directories.
- Commit the coherent source/tests/requirements/docs/evidence for this task, append
  its exact commit/checks/rulings to work/five-area/progress.md, then continue.
- Finish all five with one independent source/evidence review; fix important findings
  with fresh regression evidence and commit accepted corrections.

## Review focus

Process death before acknowledgement; inherited pipe/CPU-loop cancellation;
benchmark setup/timeout/output mismatch and incomparable solver policies; nonlinear
continuation accidentally retaining Gmin; sparse stale factors after step change;
peak-vs-RMS AC power and BE-vs-trapezoidal energy; adaptive/bias-unavailable audits.

## Completion ledger

- [x] 1 transport
- [x] 2 benchmark
- [ ] 3 difficult circuits
- [ ] 4 sparse qualification
- [ ] 5 physical consistency
- [ ] Final review

