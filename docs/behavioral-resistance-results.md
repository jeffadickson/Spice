# Voltage-controlled behavioral resistance checkpoint — 2026-11-13

The unchanged original `regression/misc/asrc-tc-2.cir` completes OP and AC on
both LU and Faer against stock ngspice. All eight observers agree in each
analysis: maximum OP absolute error 2.7755575615628914e-17, zero AC error,
no gaps. Existing bounds remain 1e-6 V, 1e-9 A and 1e-4 relative. All four
original 1e-9 assertions have zero error, reporting exits 0 with INFO success,
and the separately executed full stock original exits 0. Fresh pre-feature
assemblies refused unsupported_physical_directive/unsupported_resistor_profile.
One additional original is qualified here; cumulative Sol total five.

The native model imposes I=Vout/R(Vcontrol), with analytic output conductance
1/R and control gain -I*(dR/dVcontrol)/R. It reuses the bounded expression tree
and four-terminal stamping surface; aliased output/control terminals accumulate
both partials. Newton and converged-bias sparse AC use the same physical law.
Control is high impedance. There is no frozen-control substitution, conductance
floor or hidden clamp. Zero/domain-invalid/nonfinite/overflow trials refuse;
finite negative resistance remains an explicit active law. Independent DC
current/power audits reconstruct expression values without reading the Jacobian.
The nonlinear AC physical audit remains explicitly not_checked because public
results do not retain bias; analytical and stock-oracle checks provide scoped
independent evidence. Global convergence is not claimed.

Generated R={expression} metadata retains original assignments and ranges.
Requested preparation scales the complete resistance expression by TC1/TC2 at
T-27 C; therefore current and both partials scale inversely. Unknown/duplicate
attributes, multiple independent controls, current references, zero/nonfinite
factors and unsupported analyses refuse atomically. Requested transient and
pole-zero are explicitly refused pending separate qualification. Strict public
bridge behavior remains unchanged. The authored example exercises nonzero DC
and AC control with both TC1 and TC2: all six observers per analysis agree on
both providers, maximum AC error 1.5178830414797062e-18, and its full stock program
exits 0. It is not counted as an original.

The runtime is a fresh Rust1.99.1 Windows MSVC debug build in this run's unique
E: target, from dirty a888d81 source with recorded hashes. New claims do not use
the old cdd4560 engine; that engine was used only for the fresh baseline. Pinned
RSpice29c28a engine/builder/behavioral.rs lines69–160 guides the quotient/scaling
law. The retained Rust decision and prior SpiceSharp/ngspice reuse evaluation
remain applicable; no production dependency or runtime migration was added.

Verification: 41 relevant native tests pass (new resistor8, behavioral voltage7,
switch6, audits12, contracts3, sparse AC5). Full managed suites pass531 syntax
(1 skipped) and926 frontend (40 optional integrations skipped), with zero build
warnings/errors. The explicit native/oracle replays ran separately from those
skips. Real StrictDoc0.30.4 parsed68 documents. Proptest1.9 ran128 seeded
resistance derivative/current cases, fixed seed202611131001, plus the persisted
mutant regression. Malformed JSON/model/attribute/domain cases ran. No separate
coverage-guided fuzz process, Linux or GPU qualification ran.

Stryker5 initially killed10/13 selected temperature/factor mutants. The three
survivors identified missing TC2 expectations and diagnostic identity checks;
these were added, and the rerun killed13/13 tested, zero survivors/timeouts.
Whole-project instrumentation also produced2746 compile-error and3136 filtered
mutants, excluded from the selected result. A native control-derivative-sign
mutant built and failed6/8 tests; proptest minimized it to output=-44.02015118062074,
control=0, base=500, slope=-1.8269306128575975. Restored native regression and
coverage pass. This is scoped mutation evidence, not full-project qualification.

Coverage: syntax7305/11130 lines and2748/4746 branches; frontend/transitive
21616/27483 lines and6497/10281 branches. Native focused8-test coverage measures
1805/7834 owned library lines across47 files, including29/29 new-law lines;
stable Rust instrumentation provides no measured branch coverage. Managed scopes
include generated grammar/transitive assemblies and differ. PowerShell helper,
JSON schema and native vendor C/C++ coverage are unmeasured.

The request schema now includes the resistance tree and required AC phasor
fields. The maintained [check-jsonschema CLI](https://pypi.org/project/check-jsonschema/)
0.39.0 with [jsonschema](https://pypi.org/project/jsonschema/)4.26.0 was installed
only under E: as verification tooling; no product Python dependency. Actual8
OP/AC requests validate and6 malformed cases reject. The reusable PowerShell
orchestrator also refuses a reused output directory without changing its result.
Exact dependency versions and wheel hashes are retained in evidence. The initial
positive check exposed the pre-existing omitted AC phasor schema field; fixed
before final validation. Other historical advanced-model schema gaps remain
outside this slice. Schema acceptance does not prove resource/domain validity.

Strict Clippy with -D warnings fails four findings in unchanged
bsim330.rs139, configured.rs468 and expression.rs96/108. Normal Clippy completes
with those same four warnings; no clean strict-Clippy claim. The first StrictDoc
parse found a missing blank separator between requirements; corrected before the
68-document pass. Failed attempts remain in raw evidence.

Reproduction commands, complete observer bounds, source/runtime/input hashes,
tool versions, seeds, coverage and raw paths are in evidence/behavioral-resistance.json.
Bulk artifacts: E:/Artifacts/Spice-sol-behavioralresistor-a888d81-20261113T1015.
The schema helper is scripts/Test-BehavioralResistanceSchema.ps1. MOS memory's
stock-default mismatch, general BJT VA dynamics, arbitrary multivariate behavior
and nonlinear semiconductor temperature remain open. No pushes, merges, CI,
schematic changes or custom RAM caps were used.
