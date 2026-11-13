# Post-OP pulse parameter state verification — 2026-11-13

The unchanged pinned original `regression/misc/alter-vec.cir` now completes its
single OP, both original parameter-vector assertions (1e-15), and stock ngspice
full program. Both OP observers have zero absolute error, no gaps, and existing
voltage/current/relative tolerances unchanged. Reporting has err1=err2=0,
success=2 and exit 0. Fresh baseline refused unsupported_control_syntax.
This is one additional original program, cumulative Sol total four; its OP
physics already worked. This slice adds post-OP parameter metadata and reporting,
not physical source mutation before later analyses.

Typed control syntax identifies root voltage/current pulse targets and seven
explicit finite scalar values. Copied metadata and replacement arrays preserve
borrowed inputs and earlier reads. Admission refuses unknown/non-root sources,
other waveform kinds, wrong cardinality, before/interphase/nested effects and
non-OP profiles. The coordinator now prevents terminal assertion failure from
becoming whole-program agreement for a single analysis, as it already did for
ordered programs. The authored false assertion retains numerical OP agreement
but reports requested_control_execution_failed, whole-program agreement false,
and exit 1. Full stock ngspice also exits 1. The current-source example agrees
and full stock program exits 0. These authored cases are not original counts.

Final builds: zero warnings/errors; syntax 531 passed, 1 skipped; frontend 916
passed, 40 optional integrations skipped; restored focused syntax 11 passed.
Real StrictDoc 0.30.4 parsed 67 documents. FsCheck ran 128 cases, replay
`5006,2026111301`, checking ordered seven-field updates and borrowed ownership.
Malformed/negative tests cover invalid targets, cardinality, nonfinite values,
separators and admission boundaries. No separate fuzz process ran this hour.

Stryker.NET 5.0.0 tested/killed 8 selected binding guard mutants, zero survivors
or timeouts. Its project instrumentation also generated 2706 compile-error and
3117 filtered mutants, all excluded from this selected score. A manual stale-read
mirror mutant built and failed two tests (9 passed), including the property
shrunk to NonNegativeInt 0 after one test/one shrink; restored tests passed 11.
A second manual gate mutant restored ordered-only failure gating: the negative fixture then incorrectly claimed native_agreed=true despite failed_assertions; the fixture check detected this regression. Both source changes were restored. This is scoped mutation evidence, not whole-project mutation qualification.

Coverlet syntax scope: 7300/11120 lines (65.65%), 2742/4734 branches (57.92%).
Frontend/transitive scope: 21388/27247 lines (78.50%), 6439/10227 branches (62.96%).
Scopes include generated grammar/transitive assemblies and differ. Rust/native
coverage and this hour's native rebuild were not performed. Execution deliberately
reuses the previously certified cdd4560 behavioral engine; exact engine, oracle,
source and input hashes, commands, report paths and observer bounds are in
`evidence/source-pulse-alter.json`. Raw artifacts stay on E: at
`E:/Artifacts/Spice-sol-sourcealter-bd3bfb4-20261113T0920`.

Pinned RSpice control.rs lines1040–1130 guided target parsing and private candidate
publication; that pinned routine handles SIN vectors, not this pulse vector.
Stock ngspice on the unchanged original supplies the independent pulse reference.
The initial full syntax attempt found a missing grammar witness, fixed before
final verification. Initial authored native runs exposed missing spaces around
vector brackets; examples corrected and final whole native runs repeated.
Failed attempts remain in E: evidence.

Legacy BJT diagnostic copies still refuse unsupported_bjt_profile; selected-DC
VA support must not be promoted to general dynamics. They are uncounted.
Nonlinear behavioral resistor temperature and mosmem stock-default mismatch
remain open. No pushes, merges, CI, schematic changes or memory caps were used.

