# Constitutive constraints and energy qualification

Base ca51425c74840afa2eae1ccf46c41b840ac4d48a; production code unchanged.
The new manufactured DC states distinguish constitutive voltage equations from
conservation: an inductor or CCVS can have balanced KCL and power while its
voltage is wrong. Tests require the named constraint to exist and to fail in
that state. Behavioral voltage-source cases also check nonzero negative control
terminals, translated control potentials and the absolute-plus-relative bound.

For a correct behavioral source, actual=expected=2*control, so its known bound
is voltage_absolute+relative*2*abs(expected). Checking only a zero residual
would miss incorrect normalization. The power-scale follow-up applies the same
lesson: at 2 V, a 2 ohm load plus shunt G has source/load/shunt powers
-(2+4G), 2 and 4G W. Their signed sum is zero, but absolute sum is 4+8G W.

The stationary MOS fixture independently fixes a 75 microamp triode current.
With zero oxide charge its memoryless energy balance is measured and passes.
With 3 nF oxide charge, the nonlinear stored-energy audit remains NotChecked,
with no fabricated numeric metrics and an explanatory reason. No new nonlinear
energy algorithm or physical-model implementation is claimed.

## Native outcomes

- 20261119T040005Z-fa8f450f: 13 selected counterparts, eight caught/five missed.
- 20261119T040904Z-4817f02f: power-scale counterpart caught after adding the
  independent absolute-power-bound assertion.

Final disposition: nine distinct counterparts caught and four reviewed
currently unobservable energy-term substitutions, across 14 attempts. There
were no timeouts or invalid builds. Native roots are under
E:/Artifacts/Spice-issue61-20261117T133906Z/current-audit-lean.

The four surviving diffs only replace arithmetic in the charged-MOS local
energy term. That branch still sets nonlinear_storage=true and changes no
currents. The accumulated energy/scale are discarded in favor of NotChecked;
the changed terms are neither checked nor serialized. Under the recorded
nontrapping floating-point execution contract, they do not alter that output.
Exact frozen diffs/hashes were reviewed individually. Keep raw Missed outcomes,
retain guards, and reassess if qualification, metrics, logging, later error
behavior or execution semantics change. This is bounded source review, not
formal proof or qualification of nonlinear energy conservation.

## Verification

Five new contract tests are included in the final baseline: 51 unit and
672 integration passed, six explicitly ignored. Existing seeded property tests
ran; no fresh standalone fuzz engine, Stryker or ngspice comparison ran. The
ignored cases remain three native BSIM/SOI tests, two oracle tests and the
separately launched endurance batch, not passing evidence.

Intel's 15 routine tests and separate SpMV probe passed. StrictDoc0.31.0 parsed
113 documents and 627 unique requirement IDs. Scoped integration coverage:
audit.rs 344/579 lines under audit_output_contracts. Stable Rust branch coverage
is unavailable; test source, vendor libraries and executable main are excluded.
This suite's scope differs from earlier coverage, so percentages are not a
comparable overall trend. Full/targeted seed2026111904; coverage seed88320261103.

Independent read-only review found no important fixture or classification issue.
Exact commands, sources, native outcomes, snapshot hashes and limitations are in
evidence/epoch-audit-outputs-results.json. Raw artifacts stay at
E:/Artifacts/Spice-epoch-triage-20261119T035409Z. Frozen 30a5a9a remains unchanged;
none of these later targeted results retests all 4411 or adds an original deck.
