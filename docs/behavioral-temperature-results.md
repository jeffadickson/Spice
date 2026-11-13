# Affine behavioral temperature checkpoint

The unchanged original `regression/misc/asrc-tc-1.cir` now completes its original
OP and AC analyses and its whole reporting program. All six mapped signals agree
in each analysis: maximum OP absolute error 1.4210854715202004e-14, zero AC error.
The existing voltage/current/relative bounds remain 1e-6 V, 1e-9 A and 1e-4.
Original err1/err2 are -1.1102230246251565e-16, err3/err4 are zero, against the
unchanged 1e-9 assertions. Our reporting and the separately executed full stock
ngspice original both exit 0 with INFO success. One newly agreeing original is
added; the Sol campaign total is three. Authored fixtures are not counted.

Requested preparation now preserves generated TC1/TC2 attributes and scales the
complete affine B voltage/current relation, including its offset, before emitting
the existing typed VCVS/VCCS/source stamps. Signed and zero finite factors are
retained. One literal root .temp above absolute zero selects the Celsius parameter
environment and is published on the requested plan. Nondefault root temperatures
admit qualified linear stamps; semiconductor/nonlinear combinations, conflicting
option precedence and control constant exports refuse explicitly. DC temperature
axes override the base temperature per point and recompile qualified affine
controlled stamps. Strict non-requested preparation retains its old refusals.

The original has a conditional quit 1 after its OP assertion, before AC. Only
such failure exits are admitted early; successful or unconditional early exits
still refuse. The ordered reporter returns failure before binding a later plot.
Physical results are precomputed and validated by the caller; this bounded
postprocessing contract does not claim lazy physical dispatch or rollback of
simulation work. The authored false-assertion fixture produces exit 1, no later
reporting, and requested_control_execution_failed/native_agreed=false despite
individual numerical agreement. Stock ngspice's full negative program also exits 1.

Reuse follows the retained project decision in docs/spicesharp-evaluation.md.
Generated metadata, ParameterEvaluator, affine preparation, typed linear stamps
and ordered reporting remain shared. Pinned RSpice builder/behavioral.rs supplies
behavioral guidance, with its immutable hash in evidence/behavioral-temperature.json;
no third-party source was copied and no product dependency or native kernel added.
The authored positive example also verifies nonzero TC2 through original OP/AC
cards against stock ngspice, separately from the original-deck count.

Final restored suites: syntax 520 passed/1 skipped; frontend 905 passed/40 skipped;
66 real StrictDoc documents parsed. The suites contain 29 focused preparation and
reporting cases, two new syntax/provenance cases and a retained strict-selection
boundary. A former .temp 26 refusal expectation was updated for the newly admitted
requested profile; strict selection remains explicitly tested as refusing it.
FsCheck runs 128 preparation properties (5005,2026111301), not 128 physical oracle
simulations. The syntax suite runs 512 bounded malformed-text cases
(362,20261031), generative fuzzing rather than a coverage-guided campaign.

Coverlet syntax scope: 7158/10939 lines and 2683/4653 branches. Frontend scope:
21239/27041 lines and 6383/10102 branches. These include generated/referenced
managed code and are different scopes, not whole-product scores. Behavioral
lowering class: 51/54 lines, 62/70 branches; requested temperature class: 17/17
lines, 38/42 branches. Defensive paths remain unmeasured. The syntax skip requires
unavailable filesystem-link support; frontend skips retain external prerequisites.
The explicit original and fixture oracle runs supply separate execution evidence.
No Rust build or native coverage was added; the older certified behavioral engine
is hashed separately from the current source, not presented as a fresh native build.

Clean-baseline Stryker.NET 5.0.0 executed the selected affine scaling span:
18 generated and killed, zero survivors, compile errors or timeouts in that span.
Whole instrumentation records 2667 compile errors and 3093 ignored mutations;
these are not test-detected kills or a project-wide score. An initial attempt
contained a failing new DC-temperature admission test and is excluded from final
mutation claims. That attempt also highlighted a mixed-term overflow gap; the
new regression has one overflowing and one finite coefficient and detects the
Any-to-All mutation. The final run aborts on initial test failure and has a clean
29-case baseline. Three final manual mutations (discard root temperature, admit
nonlinear root temperature, suppress early failure return) are killed by 15, 1
and 1 tests. Earlier manual attempts containing the baseline failure are retained
but not counted. Production hashes were restored, then both full suites rerun.

All build/package scratch, TEMP/TMP, coverage, isolated mutation source, logs and
reports stay on E:. Jobs were serialized with process-tree deadlines: 600 seconds
for Stryker/final verification, 300 for managed checks and 180 for oracle/docs
replay. No custom RAM admission or memory caps were used. Bulk scripts and raw
reports are E:/Artifacts/Spice-sol-asrctemp-e3fb8bf-20261113T0805; final-qualified
contains the latest unchanged original, TC2 example and negative fixture evidence.
Full source/input/runtime hashes, seeds, commands and limitations are in the JSON.
No push, merge, CI, canonical-main, schematic or VeloSim edits. MOS memory's
stock-default mismatch remains open and is not counted.
