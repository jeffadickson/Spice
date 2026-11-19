# Expression admission and derivative checkpoint

Base `e52cf09`; production `src/expression.rs` remains byte-identical to frozen
`30a5a9a`. Five public-entry tests catch **13 original survivors** without
changing evaluator behavior, mathematical conventions or model tolerances.

Native cargo-mutants 27.1.0, profile `mutants`, default features, one candidate
and two compiler/test tasks: **13/13 caught, zero missed/invalid/timeouts**.
The owned replay package was cleaned first in a cache separate from normal
qualification and the active campaign. The unmutated baseline freshly rebuilt
the owned library and passed five tests. Exact original names/diffs, native
outcomes, source/test snapshots and executable hashes are retained. This is a
targeted current-source replay, not a retest or score rewrite of all 4,411.

The new witnesses separate structural admission from evaluation:

- Left- and right-nested binary chains admit depth 64 and reject depth 65,
  counting root depth as zero.
- A shallow 1,023-node full tree plus one unary root admits exactly 1,024 nodes.
  An additional binary root and leaf instead produce 1,025 nodes and rejection.
  This isolates node count from depth, including when counting is mutated.
- Root and nested nonfinite constants are rejected. Structurally valid
  `log(-1)` remains distinct from its real-domain evaluation failure.
- Direct evaluation independently enforces depth even without `validate`.
  Addition, product, sine and `exp(log(1))` chains include valid depth-64
  controls and deeper negatives. Values stay finite mathematically, so an
  unrelated domain/overflow failure cannot mask a missing depth increment.
- The linear-fractional expression `(2x+1)/(3x+2)` uses the independent analytic
  slope `1/(3x+2)^2`, from determinant `4-3=1`. Proptest ran **64 generated
  dyadic-input cases**, `x` in `[-0.25,4]`, seed **2026111927**. Absolute value
  and slope bounds are `1e-13`; the positive denominator excludes poles. This
  finite check does not prove arbitrary expressions or global derivative error.

Regression: **55 unit + 719 integration passed; seven ignored**. Focused and
instrumented runs each passed five tests. Regression/native environment seed
2026111928; the property pins its own seed above. Scoped expression line coverage
is **58/111 (52.25%)**, excluding test/vendor code. Other expression families,
wire conversion paths, executable main and branch coverage remain unmeasured.
Different scoped percentages are not whole-engine coverage growth.

The actual Intel routine's **15 tests** and separate UHD 630 FP64/finite/shape/
corruption probe passed (driver 31.0.101.2141, probe seed 18117). These remain
bounded hardware checks, not universal GPU qualification. Real StrictDoc parsed
**126 documents / 663 unique UIDs**. Read-only simulated review found no
important issue; its requested valid depth-64 controls were included before
the native replay. Initial and strengthened focused runs both passed.

No production bug, new original-deck/ngspice execution, Linux, Stryker,
standalone fuzz, performance improvement or full model/expression compatibility
is claimed. Existing open survivors, reviewed equivalents, invalids and the
historical timeout remain separate. Mutation-only process observations record
bounded fixtures and identities; no general memory caps or RAM gates were used.

Requirements: EXPR-STRUCTURAL-LIMITS-001, EXPR-EVALUATION-DEPTH-001,
EXPR-QUOTIENT-DERIVATIVE-001 (Draft). Bounded evidence:
`evidence/expression-boundary-results.json`. Raw artifacts:
`E:/Artifacts/Spice-expression-contracts-20261119T200826Z`.
