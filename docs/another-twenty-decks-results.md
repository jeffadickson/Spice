# Another twenty blocked originals: execution, correctness and timing

Twenty additional unchanged original files now execute every ordinary requested analysis on **Faer**. **Six agree with stock ngspice, seven differ numerically, and seven have unavailable native references.** LU executes eleven; Intel executes eight. Both have six agreeing files. These related Xyce examples retained by RSpice are outside the historical 113-file core ngspice cohort. They are twenty files, not twenty independent model families or twenty new numerical agreements.

Run: Spice-another-twenty-20261115T233944Z. Base: b0826a3dcec2ff11a3a8c2af1d72badad0e83329 with tested dirty-source and executable hashes in [bounded evidence](../evidence/another-twenty-decks.json). There is no overlap with the previous twenty. A frozen prior verified frontend/engine refused preparation for all twenty. Its initial raw baseline could not read Git identity under sandbox ownership; the previous artifacts and input hashes are retained rather than mislabelled as a new build of the dirty source.

The shared blockers were whole-card numerical options and stateless behavioral transient execution. Generated ANTLR metadata now carries TIMEINT ABSTOL/RELTOL/DELMAX/CONSTSTEP=1 into typed requests. ABSTOL bounds current convergence, not every native integration-error use; RELTOL bounds existing Newton/adaptive relative policies. Stricter physical floors remain. DELMAX caps the step; CONSTSTEP disables adaptation while retaining source corners. Unknown companions, duplicate settings and unsupported scopes remain atomic refusals. Whole reporting/debug cards have explicit omissions; DEVICE TEMP uses the existing Celsius environment and model-support checks.

Driven sharp switches now have explicit bounded rejected-Newton-trial recovery, reusing the existing adaptive controller. The representative previously failed at a source transition; a diagnostic retry experiment completed it before the regression and implementation. Original numerical controls, comparison bounds, analyses and model inputs were not weakened or rewritten. Requested constant stepping intentionally disables adaptive recovery.

Grounded memoryless B-voltage laws can run configured backward Euler when controls are directly driven by grounded independent/B voltage sources. Startup orders direct behavioral dependencies, then ordinary Newton and independent endpoint audits enforce the original equations and load currents. Domain errors remain errors. AC, storage, passive-mediated controls, cyclic startup, explicit state/continuation and other methods remain refused profiles. This is not a full electrical dependency graph or a general convergence guarantee.

## Original-file results

All twenty have complete Faer execution. “Not fully qualified” can mean disagreement or unavailable reference, never passing. The complete per-operation statuses and original hashes are in the manifest.

| Original relative path | Faer | LU | Intel |
|---|---|---|---|
| AGECAP/agecap_ref.cir | requested_executed_not_fully_qualified | requested_executed_not_fully_qualified | requested_execution_failed |
| Certification_Tests/BUG_201_SON/bug201b.cir | all_individual_requested_analyses_agreed | all_individual_requested_analyses_agreed | all_individual_requested_analyses_agreed |
| Certification_Tests/BUG_307/bug_307_d.cir | requested_executed_not_fully_qualified | requested_execution_failed | requested_execution_failed |
| Certification_Tests/BUG_307/bug_307_e.cir | requested_executed_not_fully_qualified | requested_execution_failed | requested_execution_failed |
| Certification_Tests/BUG_307/bug_307_f.cir | requested_executed_not_fully_qualified | requested_execution_failed | requested_execution_failed |
| Certification_Tests/BUG_307/bug_307_g.cir | requested_executed_not_fully_qualified | requested_execution_failed | requested_execution_failed |
| Certification_Tests/BUG_307/bug_307_h.cir | requested_executed_not_fully_qualified | requested_execution_failed | requested_execution_failed |
| Certification_Tests/BUG_311/bug_311_a.cir | requested_executed_not_fully_qualified | requested_execution_failed | requested_execution_failed |
| Certification_Tests/BUG_311/bug_311_b.cir | requested_executed_not_fully_qualified | requested_execution_failed | requested_execution_failed |
| Certification_Tests/BUG_61/capacitor.cir | requested_executed_not_fully_qualified | requested_executed_not_fully_qualified | requested_executed_not_fully_qualified |
| Certification_Tests/BUG_80/capacitor.cir | requested_executed_not_fully_qualified | requested_executed_not_fully_qualified | requested_executed_not_fully_qualified |
| MCNC_BJT_RCA/rca.cir | requested_executed_not_fully_qualified | requested_execution_failed | requested_execution_failed |
| TL1X/TL1X_mpi.cir | requested_executed_not_fully_qualified | requested_execution_failed | requested_execution_failed |
| TIA/TRAP/testmaxstep.cir | all_individual_requested_analyses_agreed | all_individual_requested_analyses_agreed | all_individual_requested_analyses_agreed |
| ACSENS/lowpass_fd.cir | all_individual_requested_analyses_agreed | all_individual_requested_analyses_agreed | all_individual_requested_analyses_agreed |
| Certification_Tests/ISSUE_543/acBaseline1.cir | all_individual_requested_analyses_agreed | all_individual_requested_analyses_agreed | all_individual_requested_analyses_agreed |
| Output/TRAN/tran-prn-noheaderfooter.cir | all_individual_requested_analyses_agreed | all_individual_requested_analyses_agreed | all_individual_requested_analyses_agreed |
| RESISTOR_TD/temp_dep_2.cir | all_individual_requested_analyses_agreed | all_individual_requested_analyses_agreed | all_individual_requested_analyses_agreed |
| ABM_EXPLN/exp_ln.cir | requested_executed_not_fully_qualified | requested_executed_not_fully_qualified | requested_execution_failed |
| ABM_LOG/log.cir | requested_executed_not_fully_qualified | requested_executed_not_fully_qualified | requested_execution_failed |

Seven switch originals reach execution here, but stock ngspice rejects nested SW model lookup. The RSpice live-reference harness also reports unavailable native data for them. No agreement is invented. Seven other files retain numerical differences; option/method interpretation and grid/integration behavior need adjudication. No mismatch is dismissed as interpolation without evidence. The RSpice conformance harness reports ten passes and ten failures, including these reference failures; its tolerances/grid procedure differ from ours, so its count is separate.

## Timings

Medians below are milliseconds, one warmup then three new child processes. All six files agree in both our and RSpice's separate ngspice qualification. Other owned builds/tests/mutants were stopped during the final measurement; unrelated OS activity was uncontrolled. Raw repetitions and ranges remain available. An earlier all-twenty timing trial under contention is diagnostic only.

| Original | Ours, prepared Faer request | ngspice, original deck | RSpice, original deck |
|---|---:|---:|---:|
| bug201b.cir | 714.07 | 1433.5 | 1939.56 |
| testmaxstep.cir | 491.6 | 982.89 | 1814.05 |
| lowpass_fd.cir | 1434.6 | 1842.93 | 1982.15 |
| acBaseline1.cir | 972.94 | 969.08 | 1157.53 |
| tran-prn-noheaderfooter.cir | 605.13 | 1088.71 | 853.4 |
| temp_dep_2.cir | 3157.13 | 738.81 | 808.03 |

These numbers **do not establish an overall speed ranking**. Ours excludes SPICE frontend preparation and includes worker startup, JSON/results and independent audits. Ngspice includes parsing and RAW output. The new public-API RSpice probe separates parse/analysis time and invokes no oracle inside timing, but links the pinned **unoptimized dev core**; optimizing glue does not make a release-to-release comparison. Repetition spreads are substantial.

Prepared-native medians are lower than ngspice on four files, overlap closely on acBaseline1, and are much slower on the six-point temp_dep_2 DC sweep. That sweep launches six workers versus one process for each reference. Persistent/batched dispatch is a concrete performance target; this evidence does not promise a particular speedup. Solver-phase time is not separately instrumented in our one-shot CLI.

## Verification

- Native root suite with Intel feature/provider: **644 passed, zero failed, five ignored**. Current targeted public/negative behavioral and relative tests also pass; the new relative property has 64 cases, seed 2026111521.
- Frontend: **1,073 passed, 42 skipped**. Syntax: **541 passed, one skipped**, reusing its unchanged compiled tests against the newly built syntax assembly. New step-cap FsCheck property: 64 cases, replay 1521,20261115. Existing malformed/fuzz/property suites ran within these scopes. Skipped native-environment integrations are not passing coverage; the original-deck coordinator supplies separate executed evidence.
- Managed four-module coverage: **23,528/28,704 lines (81.97%)**, **11,386/16,325 branches (69.75%)**. Includes generated parser code; not native solver coverage.
- Native targeted behavior scope: configured.rs **213/397 lines (53.65%)**, compile.rs 133/341, api.rs 121/389, legacy transient.rs 22/80. Relative-only scope covers configured.rs 174/397. Scopes overlap and are not added. Stable Rust branch coverage is unavailable. Timing helper/probe and GPU driver/kernel coverage are unmeasured; no fresh Stryker run.
- Cargo-mutants 27.1.0: **six selected admission-guard mutants, six caught, zero survivors, timeouts or invalid candidates**. Isolated snapshot/cache and unmutated baseline retained. The owned mutant-growth observer made no intervention; ordinary jobs had no RAM caps. These six faults do not establish a mutation score for every changed operator.
- Real StrictDoc SDReader: **82 documents, 566 unique UIDs**. Six new Draft requirements retain scope limits. Generated grammar map: 113 productions, 109 assessed, four unassessed; no blanket extension support implied.
- Actual Intel UHD Graphics 630, driver 31.0.101.2141: circuit/provider/CLI gates and OpenCL probe pass. Only **eight of twenty** originals execute; twelve fail execution. No CPU fallback, GPU speedup or general GPU qualification claim. Windows x64 was exercised; Linux was not.

Internal read-only review found and fixed the legacy Rust entry-point bypass and passive-mediated startup admission. Their regressions were observed failing before correction. The even-repetition median was corrected and checked for odd/even examples. Review was source inspection, not independent external execution. Earlier compile-invalid fixture migrations, obsolete-test expectations, missing-provider setup and sandbox bootstrap failures remain distinct from numerical circuit failures. Final gates pass; failed attempts remain local.

## Reproduce and continue

Verify the artifact directory exists before use: E:/Artifacts/Spice-another-twenty-20261115T233944Z. Final reviewed records: reviewed-faer, reviewed-lu, reviewed-intel; binaries: engine-final and runtime-reviewed. Their script arguments, original requests/responses, native logs/RAW, hashes, full tests, coverage, mutation outcomes and seeds remain local. Prior trial directories are preserved. The generated request schema was exported from actual current DTO metadata; no independent JSON Schema validator was run.

The public timing glue is evaluation/RSpiceTimingProbe.rs, built with scripts/Build-RSpiceTimingProbe.ps1 against the pinned external source/library. It adds no product dependency or model code. scripts/Measure-OriginalDeckTimings.ps1 accepts the completed qualification summary, original corpus, binaries, fresh E: output, repetition count and optional OnlyAgreed switch; every directory/input is checked. Matching compiler/dependency libraries are required. Artifact acquisition and the existing standalone remote-run workflow remain separate.

Prioritize the nine remaining LU execution failures and seven waveform differences, then qualify lower-overhead DC dispatch and a release-built RSpice reference before performance claims. Keep the seven native model-lookup failures unavailable. Broader TIMEINT algorithms, passive-network behavioral startup, AC/storage behavioral laws and general model/catalog parity remain open. Canonical main and unrelated work remain unchanged; these checkpoints stay on fix/deck-blocker-implementation.

[Updated cohort burndown](../evidence/another-twenty-decks-burndown.svg).

Results and prioritized follow-up: [issue #54](https://github.com/jeffadickson/Spice/issues/54).

Implementation commit: f1f31069cfab7090b6790b9457a578c29d14d3d9. Tests/replays/timings remain pinned to their recorded pre-commit executable and dirty-source hashes. Only trailing line breaks were normalized for publication; the subsequent rebuilt executable has a different hash and is not substituted into those historical numerical/timing records.
