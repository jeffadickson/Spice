# PSpice VSWITCH and the motivating ngspice bug883

The generated frontend now prepares a bounded smooth PSpice VSWITCH into the
existing Rust Newton and sparse AC runtime. This is a device-model extension,
not a new parser, solver dependency or general XSPICE implementation.

The motivating circuit is preserved as `compat/ngspice/bug883-vswitch.cir` plus
its library. It has RON100Ohm, ROFF10kOhm, VON0.2V, VOFF0V, control0.1V
and a1kOhm load. Midpoint resistance is1kOhm: OUT is0.05V at0.1V DC drive,
and the small-signal input gain is0.5+j0. The original upstream bug negated
AC partials, producing a negative incremental resistor and an impossible gain.

Independent references: [bug883](https://sourceforge.net/p/ngspice/bugs/883/),
local pinned ngspice `pswitch/cfunc.mod` and `inpcompat.c`. Equations were
implemented independently using the smoothstep log-resistance contract; no
ngspice implementation code is vendored. The two small fixture files retain
public reproducer circuit data and source attribution in the fixture README.

The model has ideal high-impedance control. Ngspice's pswitch adds a default
1TOhm control input resistance; the native comparisons use ideal control
voltage sources, so output voltage comparisons do not qualify control loading
or supply current parity. The bounded model requires explicit four assignments,
1mOhm<=RON<=ROFF<=1TOhm and finite thresholds separated by at least1pV.
Unsupported parameters, model families and instance state flags refuse atomically.
There are no silent clamps, coincident-threshold perturbations or defaults.

DC and AC use the same analytic signed partials for output and control voltages.
The switch contributes to DC KCL and power audits. Nonlinear AC physical audit
remains explicitly not_checked because the public result does not retain bias;
complex system residual verification and independent native comparisons still run.
Configured transient initialization now checks the complete nonlinear residual
before exposing a sample, fixing a missing-current hazard found during review.
That regression is not broad transient or switching-event qualification.

Verification is recorded in `evidence/pspice-switch-results.json`. Rust tests
cover midpoint/endpoints, negative/nonfinite inputs, output/control AC gains on
both CPU providers, full MNA finite differences, identical dense/CSC Jacobians,
128 reproducible proptest cases and initialization before observers. Managed tests
cover include/port/parameter/model binding, declaration-scope shadowing, atomic
refusal and native original/selected analyses. Two selected sign mutations are
separate from full mutation qualification. Coverage scope and missing branches
are explicit in the evidence; coverage does not establish correctness.

The original ngspice include-based sweep uses `-n -D ngbehavior=ps -b`.
Selected expanded-source oracle decks use explicit `ngbehavior=psa`, which
translates the whole deck before parsing. The generated profile is now
`generated_frontend_selected_static_op`; `selected-linear-op` remains a legacy
CLI alias for `selected-static-op`. This profile selects analyses; it does not
execute the original control language. Native original frequencies are
0.99/1/1.01Hz; selected frontend probes are1/100/1000Hz.

To repeat the owned Rust tests from an existing checkout, verify the checkout
and configured artifact root exist, set a unique `CARGO_TARGET_DIR` under that
root, then run `cargo test --test pspice_switch` with
`PROPTEST_RNG_SEED=88320261103`. Coverage reproduction uses
`scripts/Measure-RustIntegrationCoverage.ps1 -Repository <checkout>
-TargetDirectory <existing-target> -Output <fresh-output>` in a child PowerShell.
It instruments the owned library and links the test against its exact rlib;
vendor dependencies are not instrumented. Required matching LLVM tools and
previously built proptest dependencies must exist; it installs nothing.
Run the named .NET integration with the existing documented frontend corpus
DLL/executable environment variables; missing prerequisites are skips, never passes.

This is an owned five-circuit oracle campaign, not a rerun of the original928-row
corpus. Previous35 DC solves/34 native matches/7 selected AC corpus files remain
historical counts. General SW hysteresis, XSPICE, temperature/noise, comprehensive
transient behavior, SpiceSharp switch comparison, release performance and GPU
qualification remain unavailable. No new soak was launched by this checkpoint.

Final checks:481 Rust tests;95 frontend tests with no skips;406 syntax tests
with one exFAT symlink skip; StrictDoc21 documents/336 IDs/324 parent links.
Two selected cargo-mutants sign mutants were caught. Focused LLVM coverage
exercised42/42 model lines; native branch coverage remains unavailable.
The original corpus burndown retains its measured bars and adds a separate
named-fixture milestone annotation; it does not invent new corpus counts.
