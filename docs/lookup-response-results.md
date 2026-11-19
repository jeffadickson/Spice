# Public response and measurement lookup checkpoint

Base `30b9916`; seven public-entry integration tests added. Production linalg.rs,
measurement.rs and expression.rs remain byte-identical to frozen source. No
solver equation, model convention, response schema or numerical tolerance changed.

Final native replay of nine original survivors: **five caught, four missed**,
no invalid/timeouts. Three raw misses are reviewed numerical equivalents; one
diagnostic variant remains open. The isolated owned replay package was cleaned
and freshly rebuilt; the final unmutated baseline passed all seven tests.

The five kills cover independently valid storage at matrix dimensions0/257,
one nonfinite complex response component, finite-time empty measurement data,
and missing depth progress on Abs and Log evaluator paths. Scripted complex
responses are explicitly invalid boundary fixtures; actual DenseLu solves the
scalar identity for the positive numerical control. The tests do not claim
zero-valued admitted matrices are solvable.

Reviewed equivalents, not test kills:

- Abs slope multiply/divide by signum: successful children return finite x/dx.
  Zero x selects the unchanged explicit zero-slope branch; otherwise signum is
  exactly+1or-1, so both arithmetic operations preserve the numerical result.
- Both measurement bracket comparisons: finite increasing sample-axis checks
  precede lookup. Exact endpoint queries return in the earlier exact-knot loop;
  remaining comparisons cannot be equal, so strict/inclusive predicates agree.

These dispositions are limited to the current validated contracts/algorithm.
The measurement17:33range guard mutation remains **open**: code review shows it
still returns InvalidInput outside the axis, but reaches the no-bracket reason
instead of the explicit out-of-range reason and may do more lookup work. Tests
require a retained nonempty reason, not an invented exact-message contract.

The initial six-test replay produced4caught/5missed. Inspecting its actual diff
exposed the missing Abs recursion witness; the initial91sites concern Abs, not
inverse trigonometry. Added positive64/deeper65Abs controls then replayed the
entire nine-name set against the exact final seven-test snapshot. Preserve both
source/test snapshots and outcomes; no initial kill is retroactively invented.

Additional analytic evidence checks Asin/Acos against the3-4-5triangle and
circle geometry on64generated dyadic inputs[-0.875,0.875], Proptest seed2026111933.
Finite Asin(0) chains and Log(Exp(0)) pairs separate recursion failure from
unrelated domain errors. The zero slope at the Abs cusp is the explicit existing
piecewise convention, not a differentiability proof.

Regression **59unit+733integration passed;7ignored**; focused/instrumented7passed.
Native/regression environment seed2026111934. Focused owned-library coverage:
linalg53/173lines(30.64%), measurement27/28(96.43%), expression33/111(29.73%).
Test/vendor/main and branch coverage are unmeasured; other entrypoints/families
are outside this run. These percentages are not whole-simulator coverage.

Actual Intel routine15tests and separate UHD630FP64/finite/shape/corruption probe
passed (driver31.0.101.2141, seed18117). Production code stayed unchanged across
the final test-only extension. Real StrictDoc parsed129documents/674uniqueUIDs.
Read-only simulated review found no important issue in math, admission inputs,
recursion witnesses or the bounded equivalence dispositions.

One newly completed compile-invalid case was reviewed: an unsupported generated
DiodeModel Default constructor (E0277), not a product defect/test kill. No
Default implementations or frozen filters were added. Current targeted replays
do not rescore or retest all4,411frozen candidates. No new original-deck/ngspice,
Linux, Stryker, standalone fuzz, performance or global derivative qualification.

Requirements: RESPONSE-MATRIX-ADMISSION-001, RESPONSE-COMPLEX-FINITE-001,
LOOKUP-AXIS-KNOT-001 and EXPR-UNARY-BOUNDARY-001 (Draft). Evidence:
evidence/lookup-response-results.json. Raw artifacts:
E:/Artifacts/Spice-lookup-contracts-20261119T230805Z.
