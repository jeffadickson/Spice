# Issue 61 resolution

Tested product checkpoint: `2ba0a15` on `fix/deck-blocker-implementation`.
Main was not merged or changed. No CI was dispatched. Raw artifacts remain local
under `E:/Artifacts/Spice-issue61-20261117T133906Z`.

## Original campaign findings

The old-main snapshot remains `0face5844c108bbf80e2af39a2d9ec275c76f703`.
All 277 originally unattempted candidates now have executing native outcomes:
173 caught,85 compile-invalid,19 survived. Including the original23 outcomes,
the300-candidate cohort is191 caught,90 compile-invalid,19 raw survivors,
zero unattempted. Broader confirmation of154 library-only misses caught137 and
retained17 survivors. All90 invalid logs contain Rust compiler error diagnostics;
no memory, linker or disk-failure marker was found in this review.

Raw historical outcomes have not been rewritten. The19 surviving findings have
these dispositions:

* 15 findings are caught by independent regression checks. The nonlinear
 first-half candidate was verified against the exact old production snapshot
 with only the new regression added; the same regression passes on current
 code, whose macro argument has no automatically emitted direct counterpart.
* One duplicated initial-vector guard was masked by downstream validation.
  Initialization and trial assembly now share a validator, preserving diagnostics;
  its own mutation is caught without downstream masking.
* Three conditional equivalents are retained as survivors with exact native diff
  hashes, explicit assumptions and read-only review. They concern PWL parity,
  literal switch-sign factors and the exponential fall-delay equality guard.
  See [the arguments](issue61-conditional-equivalents.md). These are source-based
  dispositions, not formal proofs or test kills.

Targeted native follow-ups:24 candidates caught,zero survivors/timeouts/invalid/
unattempted (15 original findings,one shared-validator replacement,eight
supplementary checks). This includes the explicitly labeled old-snapshot proof.
The rejected neighboring-audit-predicate proof is not used; the actual stop-time
mutant now has an executing regression.

Managed follow-up: Stryker.NET5.0,204 candidates,190 killed,zero survivor/timeout/
uncovered,one compile-invalid,13 tool-filtered. The compile-invalid outcome is
preserved; its exact compiler cause is unavailable beyond the tool report.

## Changes and verification

Uniform sampling now respects its exclusive upper endpoint after rounding and
supports finite opposite-sign endpoints whose width overflows. Both defects
were reproduced before fixing. Ordinary seeded interpolation remains unchanged.
Newton input validation has one owner; no convergence tolerance was weakened.
Added independent checks cover audit stop-time, initial residual equality,
nonlinear half steps, full-MNA units, parser byte bounds, interior grid spacing,
source phase, damping, exponential delay order and pulse fall endpoints.

* 45 unit and632 aggregate integration tests passed;five explicit skips.
* 18 property methods passed with environment seed2026111701 and128 requested
 cases per generated property; methods with their own pinned seeds retain them.
 The sampling corpus separately uses seeds0..127. No counterexample remained.
* Real ngspice47+ charged-diode DC/AC/selected transient oracle:one pass,
 LU and Faer, unchanged bounds. This is not full-transient or full-corpus coverage.
* Intel circuit/provider checks:15 passed on the installed provider. Separate
 UHD630 FP64 SpMV probe:6 scenarios,11,880,960 checked values,max absolute error0,
 seed18117,9 repetitions. SpMV timings are not whole-deck speedups.
* Eight Pester framework/observer checks passed. Real StrictDoc SDReader parsed
 104 documents and596 requirement IDs. Requirements remain Draft where inferred.

The five aggregate skips are two native BSIM3 checks,one SOI FD check,the
separately launched endurance batch,and the ngspice test subsequently run
explicitly above. The native model-provider skips remain unavailable coverage.

## Coverage and reproducibility limits

Selected-test mapped line coverage: sampling77/81 (95.06%),grid15/15 (100%),
netlist98/1055 (9.29%),waveform148/276 (53.62%),configured audit151/170 (88.82%),
Newton746/1111 (67.15%) in initial_newton only. These are different selected scopes,
not whole-library figures. Stable Rust branch coverage is unavailable. Managed
Client-only line90.34%/branch92.57% is a separate scope. Earlier campaign/control/
audit scopes remain in their paired evidence; do not combine their percentages.

Bounded counts,exact native names/report hashes,source hashes,tool
versions,seeds and limitations are in `evidence/issue61-*.json`. Raw reports,
observer logs,coverage and exact commands remain in the E: root. Resume parsing
used immutable FrameworkQualifiedV3; that frozen helper runs .NET11,while product
SDK pin10.0.404 was preserved. Windows managed memory and paging; no general RAM
caps were applied after the user's correction. Mutation-only observation logged
owned workers; no runaway intervention was required.

This closes the original selected-shard findings,not all possible mutations or
all simulator compatibility work. There are zero new original corpus decks from
this task; the separate fifty-deck tally remains2/50. No full SPICE,production,
GPU speedup or full-corpus qualification is claimed. No separate new parser-fuzz
engine campaign was run; malformed-input regressions and recorded property methods
provide the current input-validation evidence.
