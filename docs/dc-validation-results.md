# Independent DC validation checkpoint

Base `ef00870`; production `src/dc.rs` remains byte-identical to frozen
`30a5a9a`. Five test-only public contracts catch **five original survivors**:
sparse RHS admission, response-vector admission, coefficient-times-solution
residual scaling, independent finite-scale rejection and the inclusive error
threshold. No production equations or tolerances were changed.

Native cargo-mutants 27.1.0, profile `mutants`, default features, one candidate
and two compiler/test tasks: **5/5 caught, zero missed/invalid/timeouts**. The
owned package was cleaned in a replay cache separate from normal qualification
and the active campaign. The fresh baseline rebuilt the owned library and
passed five tests. Exact original names, diffs, native logs, source/test and
executable hashes are retained. Current-source replay does not rewrite or
retest the entire frozen 4,411-candidate campaign.

The independent witnesses are:

- Wrong sparse RHS length and nonfinite values are tested independently. A
  tripwire provider checks that neither sparse nor dense fallback is called.
- Short, overlong and nonfinite provider response vectors are NumericalFailure;
  a correctly shaped finite exact solution remains accepted.
- Finite cancelling row terms produce a representable zero residual while their
  absolute scale overflows. Validation must fail instead of reporting zero error.
- For `A=2`, `x=3`, `b=6+delta`, the scalar componentwise error is
  `delta/(12+delta)`. Exact power-of-two row scalings preserve this reference.
  Proptest ran **64 generated cases**, exponent -20 through 20, seed
  **2026111925**; this is a finite randomized check, not an all-input proof.
- Exactly represented half-integers give residual 1 and absolute scale 10^10,
  hence the binary64 quotient equals the existing `1e-10` threshold. It is
  accepted; the adjacent larger representable solution is refused. The policy
  is unchanged and is not a new signal-error tolerance.

Regression: **55 unit + 714 integration passed; seven ignored**. Focused and
instrumented runs each passed five tests. Regression/native environment seed
2026111926; the property pins its own seed above. Scoped DC line coverage is
**45/101 (44.55%)**. This measures owned library lines exercised by the focused
validator tests, excluding tests/vendor; other DC dispatch paths, executable
main and branch coverage remain unmeasured. Do not treat this percentage as
whole-simulator coverage or compare differing scopes as coverage growth.

The actual Intel routine's **15 tests** and separate UHD 630 FP64/finite/shape/
corruption probe passed (driver 31.0.101.2141, probe seed 18117). These remain
separate bounded hardware checks, not proof that arbitrary decks run on GPU.
Real StrictDoc parsed **125 documents / 660 unique UIDs**. Read-only simulated
review found no important issue in admission ordering, representability,
independent reference equations or qualification scope.

The tripwire is intentionally not a numerical solver; the new tests qualify
owned admission and residual validation rather than vendor factorization or
conditioning. No new ngspice/original-deck, Linux, Stryker, standalone fuzz,
performance or universal convergence evidence is claimed. Existing open
survivors, reviewed equivalents and compile-invalid outcomes remain separate.

Requirements: DC-VALIDATION-ADMISSION-001, DC-VALIDATION-SCALE-001,
DC-VALIDATION-BOUNDARY-001 (Draft). Bounded evidence:
`evidence/dc-validation-results.json`. Raw artifacts:
`E:/Artifacts/Spice-dc-validation-20261119T190322Z`.
