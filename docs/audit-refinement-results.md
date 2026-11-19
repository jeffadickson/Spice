# Worst evidence and refinement admission

Baseba03ed2. This checkpoint strengthens tests and documents existing behavior;
production source and the frozen4411-candidate campaign remain unchanged.
Requirements: `audit-refinement-contracts.sdoc`. Machine evidence:
`evidence/audit-refinement-results.json`.

The successful nine-minute native run caught all9 targeted mutants, including
all6 newly triaged frozen-survivor counterparts and3 additional operators.
No missed, invalid or timed-out candidates. The frozen raw score is unchanged.
Intel15 routine tests and the separate UHD630 FP64 SpMV probe passed. Real
StrictDoc0.31.0 parsed116 documents/635 unique IDs.

## Campaign recovery

The original worker64316 stopped at 2026-11-19T06:40:06UTC, after about10h1m,
with768 terminal candidates:633 caught,129 raw survivors,6 compile-invalid.
The failing framework stage was
`framework/20261119T063915Z-e2ded23d`: Cargo could not remove the epoch's
`framework/rust-target/mutants/circuit-simulator.exe` (Windows Access denied5).
This was an unmutated build/bootstrap failure, not a product-invalid mutant.
Its logs and prior cumulative summary were preserved.

At preflight the old process was absent, the canonical lock was free and the
failed executable could be exclusively opened for reading. That does not
identify the earlier lock owner or prove a permanent fix. The precise Windows
sharing failure remains unresolved; the frozen helper was not patched.

Frozen source/config/inventory/helper identities and all source input hashes
were revalidated. The pinned Resume-MutationBacklog adapter resumed the same
campaign identity `3c4cf42f70063c253fa9ff6819ba81304333951c0c39dbb12a02b39d8cade412`,
without selecting another shard or dropping native outcomes. Worker97824 started
at `2026-11-19T06:58:06.4036526Z` under observer68840. PID start instants were
compared as typed DateTimeOffset values with DateKind String JSON decoding.
Its1439-minute session deadline is `2026-11-20T06:57:07.9688126Z`; outer observer
timeout86400seconds reserves cleanup. Recovery was confirmed by cumulative
progress beyond768, not merely launch status. Original Start-FullEpoch.ps1,
configuration, helper, earlier launch records and raw failures remain intact.

Resume evidence: original epoch root
`E:/Artifacts/Spice-mutation-epoch-30a5a9a-20261118T203254Z`, unique invocation
`invocation-20261119T065737Z`, including validation, failed summary backup,
launch identity, stdout/stderr and mutation-only observation. This is all-day
authorization, not a newly started nightly shard. Unfinished candidates remain
pending across invocations; elapsed invocation time is not total campaign time.

## Six survivor causes

Three survivors concerned selecting the worst check. A failed measurement with
either missing discrepancy or missing tolerance must outrank finite evidence.
Selection uses discrepancy/tolerance, not their product or the largest absolute
discrepancy. A deterministic tie keeps the first sample's actual metrics and
context. The first-on-tie requirement is Draft and records existing strict-`>`
behavior; it is not a numerical law. Its mutant changes observable evidence,
not just a private implementation detail.

Three survivors weakened refinement admission by requiring paired invalid
fields: finite/positive absolute bounds, supported units/nonempty coarse input,
and finite/nonnegative times. Each now has independently invalid cases with
other inputs valid. Shared negative or positive-infinite axes prevent a later
no-match rejection from hiding a deficient admission predicate. Empty coarse
input, selected-signal absence/nonfiniteness, repeated/descending axes and both
physical units are also challenged.

The normalized-order oracle uses bounded integer cross products, independent
of floating division, over128 Proptest cases with seed2026111907. A selected-point
fixture has ratios1/0.2=5 and2/1.1<2, plus an unmatched fine midpoint99. It verifies
exact shared-point comparison and an explicit warning that observed refinement
change is not a global error bound. No tolerance was weakened.

Frozen names/lines/diff hashes remain distinct from current replay names. Prior
junction-audit edits shift these current lines by10; the six mutated expressions
and worst-selection/refinement function bodies are unchanged. The targeted
inventory includes those six counterparts and three additional operator cases.
See the machine evidence for final native counts, not launch status.

## Verification scope

Six focused tests pass. Full baseline:51 unit and684 integration passed, seven
explicitly ignored. Scoped owned-library line coverage: audit89/586 lines.
Stable Rust branch, test/vendor/native-model/main coverage is unmeasured; this
named-suite scope is not an overall trend. Integer property cases do not prove
all floating-point behavior.

No fresh standalone fuzz engine, Stryker or ngspice run is claimed for this
test-only checkpoint. Existing physical production source matches the prior
ba03ed2 checkpoint, whose fresh LU/Faer/ngspice comparisons remain historical
evidence. No new original deck, solver speedup, full model/GPU/SPICE or formal
qualification is claimed. Read-only review found no important fixture or Draft
tie-contract issue. StrictDoc and Intel outcomes are retained in machine evidence.

Raw checkpoint root: `E:/Artifacts/Spice-refinement-audit-20261119T070011Z`.
Normal, instrumented qualification and mutation targets remain separate. All
new build output, snapshots, TEMP/TMP, coverage and logs stay on E:. Mutation-only
runaway observation retained; no generic memory caps/free-RAM gates, unrelated
jobs stopped, CI or main merge. API128 multiply-invalid error precedence and the
Windows executable-removal cause remain open.
