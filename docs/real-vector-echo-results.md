# Real vector echo checkpoint

The unchanged original `regression/model/special-names-1.cir` now completes its
whole reporting program: all nine mapped OP voltages/branch currents agree with
stock ngspice, the original 1e-7 assertion passes, and both captured reporting
and the separate full original ngspice program return INFO success and exit 0.
This is one newly completed original deck in the Sol campaign. The numerical
OP already agreed before this repair; the newly removed blocker was real-vector
echo substitution, not device physics. ac-zero and four PZ candidates already
passed on fresh baseline and are not counted as new work.

The existing evaluator now emits each real $& vector component in order, using
its existing formatter, matching its complex-mode cardinality behavior. The
pinned RSpice vector_words and expand_vector contracts guided the repair; no
source was copied and no dependency added. Missing, indexed and member names
still refuse atomically. Scalar, maximum 4096, empty and 4097-element boundaries
and borrowed-input preservation have regression checks.

Verification: syntax 505 passed/1 skipped; frontend 853 passed/40 skipped;
64 real StrictDoc documents parsed with StrictDoc 0.30.4. FsCheck exercised 128
vector cases (5003,2026111301); the new vector property shrinks to length 2 before
the repair. The full syntax suite also ran 512 bounded malformed-text cases
(362,20261031), providing generative fuzzing, not coverage-guided fuzzing. One
manual scalar-only mutant was killed by three regressions; source was restored
and the complete syntax suite rerun. No Stryker campaign was run.

Coverlet syntax scope: 7047/10908 lines, 2618/4615 branches. Frontend scope:
21146/26948 lines, 6232/9928 branches. Both include generated/referenced managed
code and are different scopes, not whole-product scores. Changed formatting
line 272 has 280 syntax-run hits. No Rust coverage was added. The syntax skip
requires unsupported filesystem-link behavior; frontend skips retain their
external-prerequisite conditions. See evidence/real-vector-echo.json for hashes,
all observer errors/tolerances, tool versions and raw reports.

Original voltage errors are at most 1.024e-8 V, current errors 1.667e-13 A, within
unchanged 1e-6 V/1e-9 A plus relative 1e-4 bounds. Reporting error is
[0,-1.1102230246251565e-16,0]; stock ngspice's original assertion independently
passes at its retained 1e-7 threshold. No observer or requested analysis omitted.
Native engine is the certified behavioral snapshot recorded by hash; this
managed-only change did not rebuild Rust or alter the original deck.

Bulk artifacts and reproducible PowerShell scripts are under
E:/Artifacts/Spice-sol-aczero-9ea6a95-20261113T0558. All build/restore outputs,
TEMP/TMP and coverage were explicitly E:. Jobs were serialized with 300-second
process-tree deadlines (documentation 180 seconds); no RAM admission checks or
custom memory caps were used. The initial canonical endurance lock probe was
free. Historical blocked-memory state was not treated as current authority.
No pushes, CI, merges, canonical-main changes, schematic or VeloSim edits.
MOS memory's stock-default mismatch remains open and is not counted.
