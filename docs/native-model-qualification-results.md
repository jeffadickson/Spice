# Native-model wrapper qualification checkpoint

Base `7132b5d`, with an explicitly dirty qualification-only test overlay.
Production functions, physical equations, tolerances and native assets did not
change. Tests were added in `tests/bsim330.rs` and cfg(test) modules in
`src/circuit.rs` and `src/bsim330.rs`; four Draft requirements are in
`native-model-qualification.sdoc`.

The enabled Windows bulk lane selected exactly35original BSIM wrapper survivors
and3private native-view survivors. It reused trusted host SHA256
`c10014846bbb96c74d92ea01963a55be7144db6b3565664a3b30be118c23fdfd` and OSDI
module `11d9e3df00edf728db2fc531efc030685d6cc70098a8550e3f13d1513238fd17`.
Existing bulk tests passed before edits. The new baseline passed1accessor unit
and4bulk integration tests, explicitly enabling the two ignored provider tests;
the existing64-case current/charge/Jacobian conservation property uses seed330.
Native outcome: **37caught,1missed,0invalid/timeout**,38candidates, approximately
24minutes. Exit2 reflects the survivor, not an infrastructure failure.

The remaining original `bsim330.rs:132:25` mutant changes
`h=1e-6*max(abs(bias),1)` to division. The fixed0.8V golden cannot distinguish
these choices, and conservation properties alone do not establish tangent
accuracy. After the cfg(test) insertion, the unchanged expression is at line171.
This remains **open**, neither equivalent nor a demonstrated production bug.
Independent tangent/reference evidence within a declared valid bias range is
needed before changing the algorithm or imposing an arbitrary step assertion.

A second lane selected the remaining5SOI-domain guards. It left the FD host
unset and tested the exact production guard: independent NaN/infinity and
adjacent-ULP out-of-range voltages must return InvalidInput; inclusive ±6V and
64seeded interior points may proceed to a result or mapped Unsupported error.
Native outcome: **5/5caught**, no invalid/timeout. Seed2026111919. This proves
domain/error-stage behavior, **not FD-provider execution, numerical accuracy or
dynamic charge**. Unsupported can represent any host failure; the test setup
separately establishes missing-provider availability for this execution.

Distinct dispositions across these43original names: **42caught,1open**.
They are separate current-source retests; frozen4,411outcomes remain unchanged.
The native-view test verifies D/G/S/body order and bulk/FD kind with four
distinct indices; onlyw/tox sentinel fields are compared in the copied bulk
model. It does not verify full model-field preservation or SOI physics.

Final verification: **55unit+700integration passed,7ignored**; focused native
integration4passed; domain unit2passed; Intel routine15tests and the separate
UHD630FP64/finite/shape/corruption probe passed (driver31.0.101.2141, seed18117).
Full-suite environment seed2026111920. StrictDoc parsed122documents/651uniqueUIDs.
Read-only simulated reviews found no important issue and confirmed scope limits.

Final-source coverage has separate, incomparable scopes:

| Scope | Measured source | Line coverage |
|---|---|---:|
| Four actual bulk integration tests | Rust wrapper bsim330.rs, tests excluded |89/109(81.65%)|
| Two validation-only SOI unit tests | bsim330.rs including cfg(test) lines |35/133(26.32%)|

Branch coverage, native host/model/vendor coverage and main are unmeasured.
The pure circuit-accessor unit executes but has no isolated measured coverage.
The wrapper percentage is not coverage of the compiled BSIM equations. There
was no new original deck, fresh ngspice run, standalone fuzz or Stryker campaign.

Historical coverage remains separate. A copied final-profile script initially
failed its fresh-output guard, and the first domain coverage driver lacked its
serde extern dependency. These attempts executed no tests; logs/scripts remain
preserved. Corrected final runs bind source and executable hashes without shims.
Each separate mutation invocation cleaned the owned package and verified a fresh
owned rustc baseline before candidate tests.

Raw38/native assets/provenance and final checks:
`E:/Artifacts/Spice-bsim-provider-replay-20261119T150437Z`.
Raw5: its `soi-domain` subdirectory. Final coverage: `native-coverage-final` and
`soi-domain-coverage-v2`. Bounded evidence:
`evidence/native-model-qualification-results.json`.
