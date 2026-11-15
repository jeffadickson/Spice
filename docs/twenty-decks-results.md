# Twenty blocked original decks: execution and numerical results

Run Spice-twenty-20261115T163235Z, based on f4ac7c1 with dirty tested source hashes in [bounded evidence](../evidence/twenty-decks.json). All twenty previously refused preparation with the frozen prior runtime. All twenty now execute every selected ordinary requested analysis on LU and Faer: **21 operations** because lead_min_ac requests OP and AC. **Four agree with stock ngspice; fifteen disagree numerically; one native reference fails.** These RSpice-retained Xyce originals are outside the historical 113-file core ngspice cohort. No increment to that cohort is claimed.

Implemented generated K-card extraction, hierarchy-aware winding binding, symmetric magnetic flux stamps, passive-group validation, signed winding initial currents and UIC startup. Independent physical audits include mutual flux. Added bounded abs/asin/acos and integer powers with bound formal parameters. Existing ANTLR extraction, integrator and solver interfaces are reused. Nonlinear cores, general waveform UIC, arbitrary real powers and singular inverse-trig endpoint derivatives remain explicit limits.

Prepared Faer solves refine residuals with their existing factor, at most three correction solves. Homogeneous row reconstruction retains tiny coefficients and exact zero constraints. The existing all-row residual bound remains 1e-10. Legacy direct sparse solves retain their original contract. No provider fallback or tolerance relaxation. The actual failing coupled-circuit matrix is a regression fixture.

Primary DC coordinate checks allow only bounded cumulative binary64 roundoff; counts, order, secondary coordinates and every output bound remain checked. Output bounds remain 1e-6 V, 1e-9 A and 1e-4 relative. Transient oracle interpolation is declared by the harness; numerical disagreements are unresolved, not assumed harmless interpolation.

## Original-deck outcomes

LU and Faer have identical statuses. Executed-but-unqualified is not passing.

| Original relative path | CPU result | Intel result |
|---|---|---|
| Certification_Tests/BUG_1946/bug_1946.cir | requested_executed_not_fully_qualified | requested_execution_failed |
| Certification_Tests/BUG_1957/mutindlin_even_ps.cir | requested_executed_not_fully_qualified | requested_execution_failed |
| Certification_Tests/BUG_1957/mutindlin_even_s3f5.cir | requested_executed_not_fully_qualified | requested_execution_failed |
| Certification_Tests/BUG_1957/mutindlin_odd_ps.cir | requested_executed_not_fully_qualified | requested_execution_failed |
| Certification_Tests/BUG_1957/mutindlin_odd_s3f5.cir | requested_executed_not_fully_qualified | requested_execution_failed |
| Certification_Tests/BUG_28_SON/bug_28_son2.cir | all_individual_requested_analyses_agreed | all_individual_requested_analyses_agreed |
| LEAD_CURRENTS/lead_min_ac.cir | all_individual_requested_analyses_agreed | requested_execution_failed |
| MINDUCTORS/InductorICBug.cir | requested_executed_not_fully_qualified | requested_execution_failed |
| MINDUCTORS/MINDUCTORS_IC.cir | requested_executed_not_fully_qualified | requested_execution_failed |
| MINDUCTORS/MINDUCTORS_IC2.cir | requested_executed_not_fully_qualified | requested_execution_failed |
| MINDUCTORS/MINDUCTORS_IC2baseline.cir | requested_executed_not_fully_qualified | requested_execution_failed |
| MINDUCTORS/MINDUCTORS_IC3.cir | requested_executed_not_fully_qualified | requested_execution_failed |
| MINDUCTORS/MINDUCTORS_IC3baseline.cir | requested_executed_not_fully_qualified | requested_execution_failed |
| MINDUCTORS/MINDUCTORS_IC4.cir | requested_executed_not_fully_qualified | requested_execution_failed |
| MINDUCTORS/MINDUCTORS_IC4baseline.cir | requested_executed_not_fully_qualified | requested_execution_failed |
| MINDUCTORS/MINDUCTORS.cir | requested_executed_not_fully_qualified | requested_execution_failed |
| Certification_Tests/BUG_141_SON/abs.cir | all_individual_requested_analyses_agreed | requested_execution_failed |
| ABM_ACOS_ASIN/acos_asin.cir | requested_executed_not_fully_qualified | requested_executed_not_fully_qualified |
| ABM_POW/abmpow1.cir | all_individual_requested_analyses_agreed | all_individual_requested_analyses_agreed |
| ABM_POW/abmpow2.cir | requested_executed_not_fully_qualified | requested_execution_failed |

The four agreeing originals are bug_28_son2, lead_min_ac, abs and abmpow1. Fourteen transient comparisons remain different. For acos_asin, a separate analytic check covers all six signals and 1-ohm KCL: 1,194 signal-points per CPU provider, zero engine failures; stock ngspice differs by up to 1.093058 mV. Its stock-default qualification stays failed. abmpow2 produces all 26 DC points here but ngspice and RSpice encounter nonfinite reciprocal-power startup; no numerical agreement is claimed.

The pinned RSpice runner reports 17 passes and three failures: BUG_1946 internal print-name validation, inverse-trig disagreement and reciprocal-power infinity. Its transient runner uses native reference time points, unlike our free-running comparison; counts are not interchangeable. Guidance came from pinned coupled_inductors.rs and the time-analysis runner. No new dependency or copied model implementation.

## Verification and limits

- Native full suite: 638 passed, zero failed, five ignored, Intel feature enabled. Earlier full-suite regressions while over-scoping Faer refinement remain in raw logs; final prepared-only correction passes the full suite.
- Managed frontend: 1,047 passed, 42 skipped. Syntax: 541 passed, one skipped. Managed bytes are unchanged by the final native-only scope correction.
- Managed four-module coverage: 23,423/28,603 lines (81.89%), 11,205/16,131 branches (69.46%). Generated parser may be included; separate syntax coverage not merged.
- Native coupling module: 30/30 lines in targeted instrumentation. Final prepared-refiner scope: 104/338 backend_faer.rs lines (30.77%). Stable Rust branches unavailable. These scopes are not whole-engine coverage. Independent helper and separately changed unary native expression coverage are unmeasured.
- Cargo-mutants 27.1.0: six selected mutual arithmetic mutants, six caught, zero survivors/timeouts/invalid. Snapshot mutual source matches current source; this does not qualify later refiner or managed changes. No fresh Stryker run.
- New native property checks: 128 cases, seed 2026111520. Full managed malformed-input/property tests also executed. StrictDoc SDReader: 81 documents, 560 unique UIDs. Five new requirements remain Draft with finite evidence and limits.
- Intel UHD Graphics 630, driver 31.0.101.2141: actual circuit regression/probe gates pass; final original run executes three, agrees on two, fails execution on seventeen. Thirty-second operation deadlines and genuine failures preserved. No speedup claim; CPU remains recommended.
- Authored coupled-transformer example is separate: OP and AC agree on both CPUs; transient executes but disagrees. Zero contribution to original-deck counts.
- New wire variants are additive; old workers may reject them. Custom implementations of the public managed voltage-expression factory need new methods. No stable binary-compatibility claim.

## Reproduction and next work

Raw artifacts: E:/Artifacts/Spice-twenty-20261115T163235Z. Verify directory existence before use. Frozen baseline: final-before-lu. Final runs: checkpoint-lu, checkpoint-faer, checkpoint-intel. Each retains original hashes, requests/responses, native RAW, complete signal maps and summary. Exact PowerShell commands are in Checkpoint-lu.ps1, Checkpoint-faer.ps1, Checkpoint-intel.ps1, FinalFull.ps1 and build/coverage logs. Manifest retains source and artifact SHA-256. Previous-runtime baseline and dirty source snapshots are distinguished from a clean committed rebuild.

Prioritize fourteen coupled-transient differences with convergence/grid studies and full voltage/current maps. Adjudicate stock inverse-trig approximation separately from engine equations. Keep reciprocal-power native failure unavailable. Investigate Intel failures before expanding GPU scope. Never weaken bounds or count omitted analyses. See [burndown](../evidence/twenty-decks-burndown.svg).

Results and prioritized follow-up: [issue #53](https://github.com/jeffadickson/Spice/issues/53).
Implementation checkpoint: 3abca106f658d635889b598390761dc74fd718a4. Qualification ran against the exact preceding dirty bytes identified in the manifest; commit creation is not a new numerical run.
