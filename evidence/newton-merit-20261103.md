# Newton mixed-unit globalization correction — November 3

The clean ea19c2e short launcher preflight stopped after three passing batches
(6912 cases), at pending seed 3401923026981217896. Its fourth property batch
minimized to RON=10 ohm, ROFF=10000 ohm, load=100 ohm, drive=
1.5822841689813791 V, control=0.5 V, reversed thresholds. Both DenseLu and
FaerSparseLu reproduced `Newton iteration limit exceeded` with 50 finite valid
corrections. This is an actual preflight failure, not a passing long campaign.

Independent reference: midpoint switch resistance sqrt(RON*ROFF), giving
output 0.3801486345140583 V. Recorded correction RHS vectors show imposed input
and control constraints remain 0.533 V and 0.169 V away after correction 49.
The prior line search used final acceptance tolerances as merit weights: KCL
rows with zero RHS were divided by about 1e-12 A, while voltage constraints used
about 1e-9 V. Its full Newton direction resolves the sources but initially
changes nonlinear switch current, so it backtracks to 1/8 and mostly 1/64 steps.
This is slow mixed-unit globalization, not a provider defect or a wrong property
domain. The original generator, assertions and default 50-correction bound remain.

Primary reference/reuse assessment: SUNDIALS/KINSOL v7.8.0 documents independent
solution/residual scaling to make norm components comparable:
https://sundials.readthedocs.io/en/v7.8.0/kinsol/Usage/index.html
and the LLNL KINSOL guide describes residual scaling away from the root:
https://computing.llnl.gov/sites/default/files/kin_guide.pdf
This bounded controller correction reuses the existing Matrix and Newton paths;
no solver-library migration or new dependency is needed. It is a local policy,
not a KINSOL implementation, compatibility claim or global convergence proof.

Frozen reference for row i = max(abs(rhs_i), sum_j abs(J_ij) *
max(abs(x_j),abs(delta_j)), dimensional absolute-tolerance floor).
Node references are amperes; branch-constraint references are volts. Merit is
the infinity norm abs(F_i)/reference_i. Compute references once per correction,
then reuse them for the old state and all backtracking candidates. The original
strict dimensional residual/update convergence, iteration budgets, provider checks
and final unregularized Gmin stage remain. Nonfinite products/sums/ratios and
malformed dimensions fail explicitly. A prototype used abs(x+delta), which
prematurely overflowed an existing valid-backtracking IEEE regression; its failed
log is retained. The final current-state/correction formulation passes that case
without computing an overflowing full trial just to estimate row magnitudes.

Actual focused GREEN: 100 tests passed (21 unit, continuation 18+5+3, nonlinear
34, property 18, analytical regression 1). Unit checks independently verify
opposing physical magnitudes, 128 row-unit scaling properties (seed 2026110304),
and malformed/nonfinite scaling. The pending endurance seed replay with 512 cases
per each of 18 properties passed (9216 generated cases plus saved counterexample).
The original minimized replay hash is retained in tests/regressions/property_campaign.txt:
0c1b95a3b3d7f5f0e70469d9f178aaa730313e37a6c6425d6ed40d323b385d3b.
Both providers now converge the minimized case in three corrections, no damping,
with residual infinity norm 4.336808689942018e-19 and correct input/control,
source current and independent physical audit. No new coverage-guided fuzz run
is claimed; the executed property suite includes malformed/IEEE-input generators.

Raw evidence root: E:/Artifacts/Spice-fix-20261103T140559Z/rust-switch-replay.
`red.log` is the direct two-provider failure on cd6360c production code (the
analytical test subsequently gained source-current/physical-audit assertions).
`green-focused.log` retains the rejected predicted-state scaling prototype;
`green-focused-v2.log` and `pending-seed-green.log` contain actual final-policy
GREEN outcomes. E: exFAT incremental-cache hard-link warnings fall back to copies.

Actual cargo-mutants 27.1.0 campaign completed: three selected, three caught,
zero missed/timeouts/unviable/unattempted; unmutated 21 unit + 1 analytical tests
passed. Operators: line-search reference multiplication to division, merit division
to multiplication, and DC progress comparison less-than to greater-than. The unit
scaling property minimized the injected merit mutant to exponent=2,
residual=-0.005709490715145624; replay hash
9223b931c9da75254c8a532c9b21da07cc6559a2e40dd00a6d9102b42471e849
is retained in proptest-regressions/newton.txt with explicit mutation provenance.
This is a three-operator campaign, not an exhaustive mutation score.
Real StrictDoc 0.30.4 parsed 47 documents/399 IDs/374 resolved parents; the two
new contracts remain Draft. Source changed only in Newton globalization; broader
M1 and universal convergence claims remain unqualified.

Scoped integration LLVM coverage executed the analytical test successfully against
owned instrumented code. See adjacent coverage JSON for actual per-file line
counts/hashes; unit tests and other integration targets are outside this coverage
run. Vendor source and test source are excluded. Stable instrumentation supplies
no measured branch coverage. Unit SI/negative/property checks are separately
executed evidence, not included in this integration coverage percentage.

Observed newton.rs line coverage in that scoped integration run: 329/852 (38.62%). This is the whole mapped file, including unrelated paths, not whole-suite or changed-line coverage. Exact final tested-worktree and canonical Git-blob hashes are separately retained in provenance; all inputs compare equal after CRLF normalization.
