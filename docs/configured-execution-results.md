# Configured execution survivor checkpoint

Base `2a497a7`; production `configured.rs` and `compile.rs` remain byte-identical
to frozen `30a5a9a`. Five public-entry tests add execution evidence without
changing equations, provider selection or numerical tolerances.

Four original survivors are caught: omitted seed-provider work in the solve
report, a startup/restart condition that kept trapezoidal runs in backward Euler,
lost behavioral dependency ordering, and addition in place of differential
control subtraction. The final-source native replay caught **4/4**, with no
missed, invalid or timed-out candidate. The owned package was cleaned first;
the fresh unmutated baseline rebuilt the owned library and passed five tests.

The earlier diagnostic replay requested eight original names and selected ten
candidates, including two unrelated netlist struct fields. Its actual outcome
was **four caught and six missed**. Among the eight original names, four were
caught, two are reviewed equivalents and two remain open. The two unrelated
netlist misses are retained as incidental, out-of-scope evidence. That first
replay used the test snapshot before the additional common-mode cases; its
results and source hash are separate from the final four-candidate replay.

The bounded equivalent dispositions are:

- `compile.rs:337`: the literal orientation signs are exactly +1 or -1, so
  dividing by the control sign equals multiplying by it before the unchanged
  transconductance multiplication. This is an exact-expression assessment,
  not arbitrary division/multiplication equivalence.
- `configured.rs:566`: the admitted behavioral profile requires a grounded
  output terminal. In the positive-terminal assignment the other operand is
  ground's literal zero. Adding or subtracting it preserves physical seed
  values. Signed-zero bit identity and unrestricted/future callers are excluded.

Both `configured.rs:350` skipped-counter mutations remain open. This counter
is not the fixed-grid index. Ordinary fixed-grid execution tests do not expose
its two-skip safety limit; no unreachable-path proof or product bug is claimed.

The tests independently check a 6 V resistive solution over 64 exact dyadic
grids (Proptest seed **2026111923**), preservation of a distinct authored point
near stop, a reversed-declaration log chain in both grounded orientations and
three common-mode translations, actual LU invocation counts, and VCCS polarity
against a -2 V / +2 V analytic load solution. A scalar diode/RC fixture checks
the startup BE and subsequent trapezoidal charge/conduction balance within
**1e-10 A**, independently of production residual code.

Final regression: **55 unit + 709 integration passed; seven ignored**.
Instrumented focused tests: **five passed**. Scoped line coverage is
**275/397 (69.27%)** for configured.rs and **186/341 (54.55%)** for compile.rs.
These are focused owned-library scopes, excluding tests/vendor; branch coverage,
other controller paths and executable main remain unmeasured. Do not compare
different scoped percentages as whole-engine coverage growth.

The Intel routine's **15 tests** and separate UHD 630 FP64/finite/shape/corruption
probe passed, driver 31.0.101.2141, seed 18117. This is bounded routine coverage,
not universal GPU or performance qualification. Real StrictDoc parsed **124
documents / 657 unique UIDs**. Read-only simulated source review found no
important issue in test equations, bounds or disposition scope.

Seven outstanding frozen compile-invalid logs were reviewed: five missing
Default implementations (E0277), one lost type inference after match-arm removal
(E0282), and one generated cast/comparison syntax error. They remain invalid,
not test kills; no production Default implementations were added.

An initial fixture supplied an unsupported title line to the bounded parser.
That test failure was corrected by constructing the intended typed circuit.
An inadvertently started diagnostic replay was stopped before terminal candidate
outcomes; its logs are preserved and excluded. Final replay/source identities
bind the corrected tests. No new ngspice/original decks, Linux, Stryker or
standalone fuzz execution is claimed, and frozen campaign scores are unchanged.

Requirements: CONFIGURED-EXECUTION-GRID-001, CONFIGURED-EXECUTION-STARTUP-001,
CONFIGURED-EXECUTION-RESTART-001 (Draft). Evidence:
`evidence/configured-execution-results.json`; raw artifacts:
`E:/Artifacts/Spice-configured-execution-20261119T180048Z`.
