# First shared fixes in the thirty-deck cohort

The user expanded this work to **fifty additional originals beyond these thirty: eighty total**, followed by hourly ten-deck work until 8am Pacific on November16. The target is not achieved. Tonight's midnight soak is intentionally cancelled; later nightly campaigns retain their schedule. See the ignored hourly queue for actual pending selections. Thirty original identities remain frozen in `compat/ngspice/thirty-decks-inventory.json`.

Current CPU result: **seven prepare, six execute, zero strictly agree**. Twenty-three preparation refusals remain; the seventh prepared example fails at the unbounded derivative of SQRT at zero. The six executions are progress in capability, not six passing numerical qualifications. Main and its unknown sheet-resistor changes were preserved.

| Original | LU / Faer execution | Strict stock reference outcome |
|---|---|---|
| ABM_ATAN_TAN/atan_tan.cir | Complete | Numerical difference under existing interpolation comparison |
| ABM_INT_FLOOR_CEIL/int_floor_ceil_bsrc.cir | Complete, all 21 DC points | Stock ngspice rejects INT; agreement unavailable |
| SEMIC_CAPACITOR/semicap.cir | Complete | Numerical difference |
| SEMIC_CAPACITOR/semicap_subc.cir | Complete | Numerical difference |
| BUG_374/capacitor.cir | Complete | Numerical difference |
| BUG_374/capacitor-bug.cir | Complete | Numerical difference |

All are retained RSpice corpus paths below tests/xyce/Netlists, not new additions to the historical 113-file ngspice core cohort. Related capacitor variants are files, not four distinct model families. None of the thirty originals completes on the experimental Intel backend in the bounded run. One separately authored capacitor-model AC fixture agrees with stock ngspice on LU, Faer and actual Intel; it is not an original-deck increment.

## What changed

The existing generated expression compiler and typed wire tree now carry SIN, COS, TAN, ATAN/ARCTAN, SQRT, FLOOR, CEIL and INT (truncation toward zero). Rust evaluates analytic chain slopes and independent endpoint audits reconstruct values. Rounding uses an explicit zero piecewise slope, not a differentiability claim at jumps. SQRT retains a positive-domain derivative profile; no invented zero-input slope was added to make its original run.

Capacitor preparation now retains generated complete instance fields and resolves scalar/model values, explicit L/W area/sidewall geometry, temperature coefficients, instance/model precedence, multiplicity and scale into the existing linear charge stamp. Numeric model CAP differs from the C marker flag. Unknown/duplicate companions and unsupported IC refuse atomically. No nonlinear semiconductor capacitor model or default geometry was silently added. Strict legacy lowering keeps its old admission contract.

The corpus runner keeps its historical 10000-point default. Its optional ninth argument selects an explicit ceiling within the already supported native range 1..100000, recorded as max_points. These unchanged 20000-step originals used 100000. No source stop/step settings, numerical comparison bounds or native product ceilings changed.

Example coordinator argument order: checkout, corpus, engine, ngspice, inventory JSON, fresh output, admission seconds, backend, max points. Use the existing Run-RequestedCorpus.cs entry point or the retained Current.ps1 launcher, checking directories and routing tool outputs to a fresh E: run. The new ceiling is an explicit caller choice; old invocations retain 10000.

Schema export was regenerated from actual DTO metadata. Its JSON writer needed a schema-only depth256 limit for the expanded polymorphic metadata; this does not enlarge runtime input/expression bounds. JSON Schema export is not independent schema validation.

## Review and verification

A read-only internal agent review found real mistakes, both corrected with observed red/green regressions: the wrong model capacitance key, and premature sqrt chain underflow. Follow-up review caught premature overflow in the first arithmetic correction. Final slope uses dx/(2*sqrt(x)); the denominator is finite/nonzero throughout the finite positive f64 domain. Subnormal and large-slope regressions challenge both extremes. This was internal source review, not participation by external experts.

Primary source guidance: ngspice captemp.c temperature/geometry computation and local cap.c/capmpar.c model parameter tables. Inspected local cap.c SHA256 D0361CB1AEF60CF32BEDBED3E1ED19531BA9448EFE52AC78B74130A38656341B and capmpar.c CBCDAD51C30A31339240ABBCA83384AB1FCA5B14651F0B69539BBDB57F50C224 under J:/dev/ngspice-ngspice/src/spicelib/devices/cap. No third-party model source was copied. The existing Rust/ANTLR/.NET reuse decision remains; standard-library math and existing charge stamps are reused.

- Full native suite before final edge-review corrections: 648 passed, five ignored. Do not relabel it as a final-source full run.
- Final targeted native verification: six expression tests plus four behavioral-transient tests passed. Proptest:64 cases, seed2026111601. Two newly added extreme-slope regressions passed.
- Fresh full frontend:1099 passed,42 skipped. Area-homogeneity FsCheck:64 cases, replay1601,20261117. An earlier even gamma was invalid, detected by the repository metadata contract, corrected and rerun. Reduced-discovery intermediate results were not accepted as full coverage.
- Fresh syntax:541 passed,one skipped. Real StrictDoc:84 documents,570 unique UIDs.
- Four selected cargo-mutants faults caught, none missed/timed-out/invalid. These used an isolated earlier snapshot for sine/cosine derivatives and the positive-SQRT domain guard, before final denominator refinement. This is not a whole-change mutation score. No observed runaway memory intervention. Fresh Stryker.NET and broad fuzz campaigns were not run.
- Managed coverage across Client, Frontend, Syntax and RequestedQualification:24059/29283 lines (82.16%),11705/16749 branches (69.88%). Generated ANTLR code may be included. Final targeted native expression coverage:71/111 instrumented lines (63.96%); tests/vendor code excluded, native branch coverage unavailable. These scopes overlap other runs and must not be added.
- Actual Intel circuit/provider/CLI gate and low-level OpenCL probe passed. The authored AC fixture agrees, but original-deck execution remains zero. Gate success establishes no general GPU coverage or acceleration. Windows Intel UHD630/driver31.0.101.2141; provider SHA4353e81a729f56f106212c8799b23af3e74a6b1d20ab21a00c335bcfb870e955. No Linux qualification.

## Reference and performance limits

A separate unchanged-input stock-ngspice screen produced native output for17/30 and rejected13, including Xyce-only INT/PWRS/IF, CAP/IND model-family aliases, TC syntax and .tran0. Those refusals cannot be stock-default numerical agreement. A second screen produced38 potentially usable references for additions; they still require a current preparation baseline and full analysis/signal qualification before selection or counting.

The external RSpice live-reference harness passes11/30. Source inspection of its reference/datasets.rs shows slope-gated local time-jitter and reference-envelope allowances. Its passes are not interchangeable with our strict comparator. These policies were not imported to inflate our count. Inspect both the actual integrator and reference alignment before concluding that every strict mismatch is a solver defect.

The six completed originals have one warmup/three-repeat diagnostic process times. None is numerically qualified, the first measurements overlap owned native test linking, ours consumes prepared JSON, and the pinned RSpice core is an unoptimized debug library. No overall speed ranking or speedup is supported.

| Original | Ours prepared Faer ms | Stock ngspice ms | RSpice debug-core ms |
|---|---:|---:|---:|
| atan_tan |39.36|195.24|122.01|
| int_floor_ceil_bsrc |773.19|Unavailable|75.57|
| semicap |798.83|316.59|1052.53|
| semicap_subc |716.67|242.79|902.17|
| capacitor |706.10|258.47|1234.93|
| capacitor-bug |935.30|273.72|985.88|

The DC row launches21 native processes; batching remains a measured overhead target. Timing inputs are an explicitly mapped projection of the six completed original rows, preserving original hashes and request ordinals. Other rows were not counted as faster failures.

## Reproduction, provenance and next work

Raw artifacts remain E:/Artifacts/Spice-thirty-fixes-20261116T030107Z. `current-faer`/`current-lu` use frozen `engine-current` (SHA cd6c482cfd52874c8cdc10ef5b2583d5f0147cbeb6b0f21e1e0cf626ab1d4e00) and `runtime-reviewed`; their complete source/artifact/input hash gate passed. Tested working bytes were dirty relative to base49c1ff4 and are retained as hashes, not misrepresented as code contained in that base commit. `final-intel` used the preceding frozen engine413b986d..., before the final sqrt denominator correction. `authored-current-intel` and the explicit final circuit gate separately cover current source. Diagnostic timings also used413b986d...; do not substitute the newer binary hash.

The direct oracle screen is OriginalNativeBaseline.ps1; final execution launchers are Current.ps1 and Final.ps1. Full command/tool/seed detail, coverage and bounded outcomes are in `evidence/thirty-decks-first-fixes.json`. Raw earlier failures, reduced-discovery tests and interrupted bootstraps remain local. Source/main and unrelated files remain unchanged. No new soak, CI or main merge occurred.

Continue multiple controls/time laws, inductor and capacitor IC/model profiles, plus the unresolved strict waveform comparisons. The original thirty remain in scope and fifty more must be independently selected and qualified. The hourly queue must distinguish selected, executed, agreeing, unavailable and unfinished work; neither eighty nor ten per hour is a fabricated success quota. Commit coherent verified slices and push only the existing development branch.
