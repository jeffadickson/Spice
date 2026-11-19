# Junction audit stability and boundary findings

Base: fdc34ff. This checkpoint fixes audit reconstruction and typed validation;
it does not change the device solver or the frozen 4,411-candidate campaign.
Requirements are in `audit-charge-boundaries.sdoc`; bounded machine evidence is
`evidence/audit-charge-boundaries-results.json`.

## Reproduced unmutated defects

The independent depletion integral used `1-pow(...)`. At a tiny finite voltage,
rounding erased charge: a grading-zero 3 uF junction at -1e-18 V over 1e-15 s
has the exact capacitor displacement -3e-9 A, but the original audit reconstructed
zero and falsely failed KCL against the unchanged default 1e-10 A absolute bound.
The new value-only reconstruction uses standard-library log1p/expm1. No device
charge evaluator, derivative or assembled solver matrix supplies the expected
values. Six deterministic steps plus 96 Proptest cases use the closed-form C*V
law, fixed seed2026111906, with zero-current corruptions as negative controls.

The coupled Early charge domain was only used inside the forward-BE branch.
At zero/reverse BE, unsupported BC>=VAF could return a report instead of a typed
domain refusal. Finite node voltages can also overflow their differential or
modulation factor. Validation now precedes the branch, and final charge must be
finite. Both backward Euler and fixed-step nonlinear trapezoidal propagate the
typed errors; supported stationary controls remain accepted. Adaptive audits
with unavailable midpoint history remain outside this qualification.

The product TT*IS could overflow before multiplication by expm1, even at zero
bias where I=Q=0 and TT*dI/dV is finite. The independent calculation now forms
physical current first. Its separate red test used IS=VT=TT=1e200 and C=0.
This is a finite-arithmetic stress test, not realistic device qualification.

`red-tests.log` records both original failures. A later transport red run records
two passing tests and the remaining overflow failure before its fix. Transient
compile failures (the shared trapezoidal adapter's changed return type and an
unannotated property tuple) are retained separately; log filenames containing
"green" do not imply those attempts succeeded.

## Two frozen survivor mappings

- Frozen audit588 `<` to `<=` maps to current audit597. It is observably wrong
  at the represented FC*VJ transition: VJ=8.106362047980708 and FC=nextDown(1)
  give `(FC*VJ)/VJ != FC`. Near the depletion singularity this is a material
  charge change, not a harmless final rounding bit. For grading3/4 the forward
  boundary constant has an independent nested-square-root integral. The retained
  first sample of seed2026111906 catches the current counterpart at default bounds.
- Frozen audit576 `>` to `>=` maps to current audit583. The current native outcome
  remains Missed. After unconditional finite BC/modulation validation and the
  finite-charge gate, the additional BE=+/-0 branch contributes only signed
  zero. Current public BE/trapezoidal current and KCL accumulators absorb those
  zero signs; nonzero charge differences, constraints and reported energy
  qualification are unchanged. Exact current diffs were reviewed independently.
  This is a bounded **current validated public physical-report equivalence**
  disposition under default nontrapping f64 semantics, not bitwise/private-Q
  equivalence, formal proof or global equivalence of the unchecked frozen source.
  The unmutated domain-admission defect was fixed rather than hidden by that label.

The successful separate cargo-mutants27.1 run tested eight current candidates:
seven caught, one raw Missed with the bounded disposition above, no invalids or
timeouts. It includes the two mapped boundaries and six new admission predicates.
Frozen original names/diffs/hashes and current mappings remain distinct. No
filters or frozen source/config/helper were changed, and no 4,411-cohort retest
or rewritten raw score is claimed.

## Verification and limits

- 51 unit and 678 integration tests passed; seven ordinary skips remain explicit.
- Six focused tests passed, including 96 randomized cases with seed2026111906.
- Two existing separate-process ngspice-47+ regressions explicitly passed using
  both LU and Faer: depletion/transport DC, AC, transient and three common-mode
  translations. Floating-case maxima: DC 8.27e-10 V/4.57e-13 A, AC component
  7.00e-10 V/5.00e-13 A, selected transient 6.00e-5 V/6.00e-8 A. Existing bounds
  remain 2e-8 V/2e-11 A DC, 2e-6 V/2e-9 A AC, 0.002 V/2e-6 A transient.
  These are existing regression fixtures, not newly supported original decks.
- Intel15 routine tests plus the separate UHD630 FP64 SpMV probe passed.
- StrictDoc0.31.0 parsed115 documents/632 unique IDs.
- Scoped library coverage: audit244/586 lines; configured audit78/170 lines.
  Stable Rust branch coverage, test/vendor/native-model/main coverage are
  unavailable. This named-suite scope is not an overall coverage trend.

No new standalone fuzz engine, Stryker, SOI/native model qualification, formal
proof, full SPICE compatibility or performance improvement is claimed. The
31-second oracle duration includes build and both engines, not a per-engine
benchmark. Reuse choice: stable standard-library functions already used in the
bounded device law; no new dependency or subsystem replacement was needed.

Raw root: `E:/Artifacts/Spice-charge-boundary-20261119T055407Z`. All build targets,
TEMP/TMP, mutation snapshots, oracle outputs, coverage and logs remained on E:;
normal and mutation caches were separate. Mutation-only process observation
retained; no generic RAM limits/gates, unrelated jobs stopped, CI or main merge.

Reproduce from a directory-checked checkout with a run-specific E: TEMP/TMP and
an isolated non-mutant CARGO_TARGET_DIR using:

```text
cargo test --profile mutants --test audit_charge_boundaries
cargo test --profile mutants --lib --test mutation_suite --features mutation-suite
cargo test --profile mutants --test spice_diode_oracle -- --ignored --test-threads=1
```

The last command requires existing verified SPICE_NGSPICE and an existing E:
SPICE_ORACLE_ARTIFACT_ROOT. Coverage used Measure-RustIntegrationCoverage.ps1
with Test=audit_charge_boundaries and a separate qualification target. Exact
mutation argv, native logs/diffs and hashes are retained under `mutations/`.
