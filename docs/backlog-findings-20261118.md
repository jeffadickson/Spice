# November 18 mutation findings

The frozen 273-candidate backlog completed in 8,746.02 seconds: 159 caught,
21 survived and 93 compile-invalid, with no pending candidates or timeouts.
This was old production revision `0face5844c108bbf80e2af39a2d9ec275c76f703`
with the explicitly recorded qualification-only test/profile overlay. It is not
an untouched clean-main run, nor qualification of subsequent development work.

## Common causes and changes

The 21 native diffs were mapped to current source by function, replacement and
removed source line, with the original column disambiguating two repeated
operators. The mapping and native diff paths are retained in the paired evidence.
Most gaps were inclusive limits and independently invalid operands: 256-byte
identifiers, 220-byte generated bases, 64-byte hash domains, four grid dimensions,
4,096 PWL points, FM deviation, finite measurement endpoints and positive solver/
audit tolerances. New tests include UTF-8 byte boundaries and the next invalid
value, rather than checking only ordinary short names.

Numerical tests independently check exp(a*x) values and derivatives, including
zero and nonunit inner slopes. A 64-case Proptest run, fixed seed 2026111802,
compares slopes against central finite differences on a bounded finite domain
with absolute error below 1e-7*(1+abs(derivative)). The trapezoidal capacitor audit
uses C=2F and differential voltage 1->3V in one second: current=4A, stored-energy
change=8J. Common-mode offsets 0,3,-2V preserve that identity; corrupting source
current must fail. No solver-generated state is used as the audit oracle.

Dynamic Newton tests check a stationary state needs zero corrections, a driven
linear step has undamped reporting and a small final update, and private
line-search validation rejects an incorrectly sized correction even when the
state/RHS sizes match. The zero-fall PULSE test distinguishes the right-hand
edge from a continuous fall: exactly at the width boundary the value is low.
A single-point parenthesized PWL source must be accepted; an odd-length list
must be rejected. Production numerical expressions were already correct; the
change adds regression coverage rather than changing physics to make mutants fail.

## Two reviewed numerical equivalents

At exactly zero MOS drain-source voltage, drain and source potentials coincide,
Id=gm=gmb=0, and swapping the mode leaves gd and all physical terminal slopes
unchanged. Signed zero is intentionally not treated as a different physical
current; this is a numerical-contract disposition, not byte-identical JSON proof.
Tests check both polarities, cutoff/inversion and independent finite differences.

For DC line-search attempt>0, alpha=2^-attempt<1 and 0<=limit<=1. Consequently
limited_candidate already returns damped=true. Replacing the redundant OR term
attempt>0 with attempt<0 cannot change the reported result. A unit test exercises
every permitted backtracking exponent; the algebra supplies the invariant beyond
a few chosen circuits. Neither equivalent is to be counted as caught.

## Why 93 were invalid

All 93 raw logs contain compiler/type diagnostics. Ninety-one are generated
function-body replacements using SimResult::new/from/from_iter. SimResult<T> is
a Result<T,SimError> alias, not a container supporting those synthesized payload
constructors. The breakdown is 42 new,24 from,25 from_iter. Two additional
replacements require Default for BackendCapabilities, which deliberately has no
context-free default. These are invalid generated programs, not 93 product
compile failures, infrastructure failures, survivors or test kills.

The pinned cargo-mutants 27.1.0 configuration now excludes only the reviewed
alias-constructor replacement shape. The framework snapshots .cargo configuration
and its self-test checks inclusion. A maintained-tool inventory fixture proves
that standard Result-returning and arithmetic replacements remain available;
the unmutated control builds and all three alias constructor forms fail real
cargo check with expected Rust diagnostics. Excluded candidates are filtered
coverage, never kills. Do not derive a misleading score improvement from the
changed denominator. Requalify the workaround when upgrading the tool or changing
the alias. The two capability Default replacements remain separate invalid evidence.

## Verification and limits

Final mapped21 replay:19caught and2raw survivors with reviewed numerical-equivalence dispositions. Both supplementary field-deletion mutants were subsequently caught in a separately recorded2-candidate replay. Full counts and per-candidate dispositions are recorded in evidence/backlog-findings-20261118.json.
The replay uses a frozen current-source snapshot, profile mutants, library plus
the focused integration target, two compiler/test workers and one mutant at a
time. Two supplementary netlist field-deletion candidates emitted by the pinned
tool are retained separately from the 21-survivor denominator. All builds,
TEMP/TMP, mutable mutation targets, coverage and raw logs stay on E:.

Full Rust regression: 47 unit and 650 integration tests passed, six explicit
integration skips. Focused integration: 13 passed plus one imported-transient execution test; private Newton contracts: two
passed. Pester: four aggregation/worker-admission checks and one alias workaround
check and 12 backlog-controller checks passed. Actual Intel circuit/provider gates and the separate SpMV probe
passed on UHD Graphics630; these are routine hardware checks, not a performance
win or qualification of every new fixture on the GPU. No new original corpus
deck is counted, no tolerances were weakened, no production numerical expression
was changed, and no full SPICE/GPU/production compatibility claim is made.

LLVM instrumentation measures the owned library under the focused integration
suite, excluding test/vendor source. Per-file line metrics and exact hashes are
in the evidence JSON; they are not whole-suite coverage. Stable Rust branch
coverage is unavailable. The private cfg(test) helper and modified C# snapshot
adapter have executed checks but no measured line/branch coverage in this run.
No separate fuzz engine or Stryker campaign ran; malformed-input regressions,
the library-based randomized test and real Rust mutation replay are the relevant
checks for this checkpoint. Read-only code review found no important correctness
issue; these were simulated agent review perspectives, not external experts.

Two supplementary native field-deletion mutants initially survived because the
focused import test checked acceptance without checking execution metadata. The
new imported_transient_contract fixture checks both supplied directive forms,
the actual 2V history, final time and maximum gap. These two are tracked outside
the original 21. The first queued supplemental launcher refused a timestamp
string/DateTime representation mismatch before launching tests; that attempt is
retained and the dependent launch was corrected to compare typed UTC instants.

A separate controller regression demonstrated that reparsing a JSON Z-suffixed
UTC DateTime as culture-formatted text can extend a finish deadline on this
Pacific host. Four parameterized cases failed before the typed DateTimeOffset
conversion fix and all 12 controller checks passed afterward. Campaign-start
restoration uses the same typed conversion. The original 273 replay had no
FinishBy value and completed normally; its recorded results are unaffected.
