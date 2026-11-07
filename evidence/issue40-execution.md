# Issue 40 execution and zero-port observables

All five selected original input/hash identities executed after the patch.
The four amplifier variants previously failed with LU componentwise backward
error one. A captured 13-unknown correction system showed a grounded source
correction of about 5.6e-63 V despite the original homogeneous singleton equation
requiring zero. Dense LU now enforces that equation exactly before checking
the complete original matrix. Nonfinite solutions are refused first and the
acceptance tolerance and correction count are unchanged.

The zero-port deck compares v(x1.hidden) = 1 V and i(v.x1.v1) = -1 mA
with zero observed difference. Native hierarchical names are recorded only
for original nodes. If no root nodes exist, those internal observables become
the comparison scope. Ambiguous rendered hierarchical names refuse.

The measured change is four additional executions and one additional agreement.
The four amplifier transients still have numerical disagreements. On the 2 ms
variant the largest voltage difference is 0.0013886983 V at the collector.
The ideal input sine differs by 0.0003978907 V, reproducing the earlier
interpolated-reference sampling symptom. Remaining dynamic trajectory/model
differences have not been isolated or waived.

Raw evidence: E:/Artifacts/Spice-issue40. The five-path summary SHA-256 is
be824a088f9d49167c7cee7bbd93e6f21847db365c1ae595d24ef2ca58713fe5.
This replay is scoped to the five issue paths. The complete 928-path census
was not rerun. The source hashes remained fixed throughout execution.

Verification passed with 545 Rust tests (two default oracle skips), 539
frontend tests (36 opt-in skips), and 51 StrictDoc documents. The captured
correction test failed against the original LU and passed with exact
singleton enforcement. The zero-port test rejects wrong voltages, wrong
currents and ambiguous hierarchical names. Scoped Coverlet reports
RequestedComparison at 76.19% lines and 43.29% branches. The instrumented
captured Rust test covers 55 of 278 mapped backend.rs lines. Stable Rust
branch coverage is unavailable in this run. No new full mutation campaign ran.
