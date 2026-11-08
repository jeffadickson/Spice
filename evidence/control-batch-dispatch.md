# Batch control dispatch evidence

Baseline 9ce8c80 refused both original if-batchmode.cir paths at
conditional_before_last_analysis. The batch profile admitted the original
root UIC transient in both paths. The paths are byte-identical. This is one
distinct control blocker and two newly executable corpus entries.

Both unchanged native decks took the batch echo branch, exited successfully
and produced RAW values byte-identical to the isolated native projections
after the Values marker. The unsupported interactive actions did not execute.
The engine completed both requested transients. The existing comparison still
reports numerical disagreement, with a maximum output discrepancy of
287.98 µV and source-current discrepancy of 287.98 nA.

The matched replays used the pinned ngspice corpus and the same prebuilt engine.
Both reports have valid source provenance. Artifacts live under
E:/Artifacts/Spice-eight-ctrl-9ce8c80. Baseline summary SHA-256 is
65aec89acd65dceff7b40a167fa21675bb3f74f0284054d533ea8ec24cc4c96e.
Updated summary SHA-256 is
aa15be815bc5c6a3dd0bbe4ec9b7e13ae4c3900db228aace27d7910333ec01bc.

633 frontend tests passed with 37 opt-in skips. Managed coverage measured
77.57% line and 61.54% branch. The baseline failed the positive regression
and passed six negative tests. The active-branch guard mutation was caught by
two tests. Existing seeded properties ran. StrictDoc parsed 53 documents,
438 requirement IDs and 406 resolved parents. Rust coverage and standalone
fuzzing were outside this checkpoint.
