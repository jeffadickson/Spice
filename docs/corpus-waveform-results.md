# Source-waveform execution checkpoint

The unchanged original928-input inventory now yields51 selected static solves,
50 with exposed root voltages independently matching native ngspice on both
CPU providers. One solved zero-port circuit has no root observable. Eight
hash-pinned exclusions,30 resource refusals and839 lowering refusals remain.
Previously38 solved/37 compared; this is13 additional solves and comparisons.
Fourteen files now match selected AC probes at1,100 and1000Hz, previously10.
Original commands, sweeps and control programs are not executed by this profile.

The generated source grammar now retains PULSE, SIN, PWL and EXP fields.
Static preparation evaluates their initial values in lexical instance scope;
explicit DC takes precedence. Transient selection is a separate bounded
operation using existing Rust waveform/runtime contracts and named defaults.
It refuses distinct DC/time-zero biases and overlapping pulse timing rather
than silently changing them. No replacement parser or solver was added.

Independent ngspice replay found and fixed a pre-existing delayed-SIN defect:
phase90 with offset0/amplitude1 must hold1V before delay; Rust held0V.
The waveform and legacy basic-importer normalization now agree. The basic
importer's whitespace SIN syntax remains its existing bounded syntax.

Verification:140 frontend tests passed,406 syntax tests passed with one exFAT
link test skipped. FsCheck source-clause permutation property ran64 cases,
replay929,20261103. Targeted Stryker replay killed61 viable mutants;49 compile
errors and303 ignored mutations are separate, with no remaining survivor in
that scope. This is not a whole-product mutation score. StrictDoc parsed21
documents,339 IDs and327 resolved parent relations. Exact source hashes,
summary fingerprints and coverage numerators/denominators are in
`evidence/corpus-waveform-results.json`. Full Rust regression and focused
native coverage are recorded there after completion.

Coverage includes generated syntax code and dependencies in the instrumented
managed projects: frontend-suite9846/17551 lines,1802/3625 branches;
syntax-suite5233/7347 lines,1879/2563 branches. These differing scopes are not
comparable scores. Native stable Rust branch coverage remains unavailable.

Raw runs remain in the unique E: artifact directory listed in the evidence.
Campaign wall times include harness/process startup and are not solver speed
benchmarks. Remaining common blockers include XSPICE and semiconductor model
families, physical options and incomplete resource graphs. No full SPICE,
full-corpus transient, Linux, GPU or production qualification is claimed.

Full Rust regression:482 passed,0 failed (seed88320261103). Focused native coverage exercised66 ngspice importer/waveform tests; per-file numerators and denominators are in the evidence, branch coverage unavailable.
