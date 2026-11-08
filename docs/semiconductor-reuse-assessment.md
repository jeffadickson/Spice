# Semiconductor kernel reuse assessment — 2026-11-02

This is an isolated assessment at simulator source `224d044`. No production dependency, migration, model-parity claim, or requirement closure is introduced.

## Recommended direction

Do not handwrite broad Gummel–Poon/BSIM implementations. First qualify and license a reusable Rust kernel. Thevenin is the strongest discovered Rust candidate for a bounded extraction experiment; the inspected kernel compiles separately from its simulator. Its available source has material parameter/charge gaps, and the crate archive lacks a license/notice text despite its BSD declaration. Production copying therefore remains deferred pending provenance and qualification. SpiceSharp 3.2.3 is the fallback MIT equation-reference/port source for diode, BJT and MOS levels 1–3; importing its simulator is outside scope. Existing Rust-owned analyses and selectable numerical providers remain the integration boundary.

## Exact candidates and primary evidence

- [Thevenin](https://github.com/cramt/thevenin/tree/f8e0b76899d1a0a7e3a9f2d673d1979b581bb1c9): source commit `f8e0b76899d1a0a7e3a9f2d673d1979b581bb1c9` (2026-07-12), workspace/crate 0.5.0. `thevenin/Cargo.toml` declares BSD-3-Clause. The recursive repository tree and downloaded crates.io 0.5.0 archive contain no LICENSE/NOTICE file. This does not establish sufficient upstream notice provenance for redistribution. README claims diode, GP/VBIC and many MOS levels; those claims are not qualification results. The diode/BJT/MOS1 equation modules primarily depend on resolved ModelParams, physical constants and junction helpers; their matrix stamps are separate functions but share files with kernels.
- [Spice21 0.1.5](https://docs.rs/spice21/0.1.5/spice21/): crates.io release 2020-12-27, BSD-3-Clause declaration, archive checksum `275a747df7eba7948a178a0239fdefdb2833a9d7b4ca997cbe001f990192b8d4`. Docs expose diode/MOS1/BSIM4 types but no BJT. Original and referenced successor GitHub URLs returned 404 during this assessment. Available docs/source are useful historical reference; maintenance, exact BSIM completeness and platform execution are unqualified.
- [SpiceSharp 3.2.3](https://github.com/SpiceSharp/SpiceSharp/tree/34484fa6c66d636f6983626a0a770f75c0484553): tag commit `34484fa6c66d636f6983626a0a770f75c0484553`; exact-tag MIT LICENSE inspected, copyright 2017 svenboulanger, retain notice. Existing repository assessment already executes limited diode/NPN/MOS1 cases. Its existing level54 rejection remains a gap; a C# equation port requires derivative/state and default/temperature auditing rather than copying simulator behaviors wholesale.
- [OpenVAF Reloaded](https://github.com/OpenVAF-Reloaded/OpenVAF): compiler LICENSE inspected GPLv3; README states OSDI0.4 versus original0.3, native Windows/Linux binaries, separately licensed integration models and GPL fallback for unmarked tests. Compiler licensing must be distinguished from model-source and generated-library licensing. An OSDI adapter aligns with the existing architecture but requires ABI/version/layout validation, dynamic loading, callbacks, model parameter/type metadata, sparse mapping and trial/accepted state. It is a larger separate slice, and would not automatically supply legacy SPICE model-card compatibility.

## Executed bounded native experiment

Artifact directory: `E:/Artifacts/spice-semiconductor-reuse/bd11f7570344446ca2e6bf5a14d0598b` (local disposable outputs, no copied upstream source committed). Rustc `1.99.1 (58cf8f9c3 2026-10-13)` compiled inspected Thevenin diode/BJT/MOS1 kernels. Extraction removes engine instance/stamp sections, excludes upstream tests, and replaces only resolved ModelParams with a two-field equivalent. There is no substituted model equation. Eleven DC/charge sample points executed. Five additional harness tests passed: forward-diode finite-difference conductance (three biases), MOS1 analytical region currents (three biases), BJT charge diagonal finite differences (three junction pairs), and two explicit limitation reproducers (unknown parameter ignored; MJE=1 produces NaN). The latter passes document defects, not acceptable production behavior.

Compile: `rustc --edition 2024 probe.rs -o probe.exe`; tests: `rustc --edition 2024 --test probe.rs -o probe-tests.exe`, then `probe-tests.exe --nocapture`. Initial test compile failed because original diode tests depended on approx and a larger ModelParams; excluding those upstream tests resolved the harness dependency. This result establishes kernel separability and finite sample behavior only. No complete-circuit DC/AC/transient oracle run, malformed-input fuzz campaign, mutation campaign, coverage measurement, Windows/Linux matrix or NativeAOT applies to the candidate yet. No production code changed; coverage is unmeasured.

## Material inspected gaps

- Unknown model parameters silently ignored. Binding must validate a declared allowlist and reject unsupported non-default semantics before model evaluation.
- Diode VT uses legacy KBOQ while BJT uses KOVERQ; temperature/constants must be explicit and reconciled against the frozen reference executable.
- BJT compute_charges returns only Qbe, dQbe/dVbe, Qbc and dQbc/dVbc. GP base charge depends on Vbc: complete charge Jacobians need cross derivatives. XTF behavior is explicitly assumed zero. MJE=1 leads to division by zero/NaN instead of a logarithmic limit or validation error.
- MOS1 uses SPICE Meyer capacitances, including integration history rather than a single globally conservative Q(V). This requires an explicitly scoped history-dependent compatibility adapter. Do not label constant capacitors or a static-Q substitution as Meyer transient parity.
- Existing simulator model.rs has linear TwoTerminalModel and bounded Shockley/constant-C DiodeProfile, rather than the architectural general F/Q/J contract. Existing AC/transient loops must consume real model derivatives/history consistently; frontend lowering alone cannot deliver semiconductor analysis parity.

## Integration API recommendation

Introduce an immutable, validated bound-model descriptor with exact family/level/version and effective model/instance parameters. Expose local terminal/internal unknown mapping, static residual F and full dF/dx, charge Q and full dQ/dx where conservative, and an explicit alternative dynamic-history capability for legacy Meyer. Evaluation receives trial local x, temperature and analysis mode; returned values have SI units and declared sparsity. Separate accepted history from trial history and commit it only after timestep acceptance. Keep parameter name/default/level policy in elaboration, outside Newton. Preserve external D/Q/M names and model binding metadata in results while private internal nodes remain implementation details.

A diode/BJT primitive composition is useful for an explicitly bounded ideal profile, but does not close GP, charge, temperature or vendor-model compatibility. Use native analytic compositions as independent checks rather than as a substitute for corpus-driven acceptance.

## Preliminary corpus inspection

A lexical scan of tracked `C:/dev/ngspice-ngspice` files with `.cir/.sp/.spice/.deck/.ckt/.net` extensions, joining continuation lines, found model declaration occurrences D174, NPN122, PNP30, NMOS108, PMOS90, VDMOS82. MOS level occurrences (N/P combined): default55, level1=16, 2=3, 3=2, 4=1 (scientific spelling), 5=1, 6=4, 8=24, 14=4, 49=36, 54=52. NPN/PNP: default104, level1=32, level4=12, level9=4. This scan is not the frozen semantic inventory: it can count duplicate definitions and cannot establish effective scope or used-model counts. Existing stored family inventory reports decks D57, NPN77, PNP18, NMOS91, PMOS82 and VDMOS50. Do not infer solvable deck coverage from either count.

Frequent declaration parameters include NPN CJC102/CJE97/BF95/IS92/RB85/TF59/TR59; diode IS101/BV83/RS54/VJ48. These show that charge, resistance, breakdown and temperature behavior cannot be broadly dismissed. Frozen model-binding snapshot counts are pending.

## Frozen semantic snapshot follow-up

Frozen waveform summary `E:/Artifacts/Spice-next-five-20261103T023139Z/corpus-waveform/summary.json`, SHA256 `965bb1d93796f1e048c7abe01c8d4984af8b234ac39cd21a02b1a08b2a9616c9`, has928 rows. It records D1206/Q1464/M8623 observed instance bindings (not unique effective model definitions). Deduplicating model family once per root row gives D74,NPN82,PNP17,NMOS113,PMOS86. Parameter names per root row, deduplicated within D/Q/M family: D/IS62; Q/CJC70,CJE67,IS60,BF53,TF52,TR52,RB50; M/LEVEL99,TOX74,VERSION60,VTH059. Missing/unbound model-family fields are excluded from family counts. This snapshot contains parameter names without values, so exact effective MOS level frequencies cannot be recovered from it; the earlier lexical declaration-level counts retain their stated limitations. These are occurrence inventories, not solvable/qualified coverage.

## Decision after native experiment

Proceed with the exact MIT SpiceSharp-derived diode charge vertical slice described in semiconductor-profile.sdoc, rather than redistribute Thevenin source with incomplete notice provenance and inspected model gaps. Native Rust analyses and providers remain. The first slice retains explicit parameter refusals and does not close broad diode/BJT/MOS compatibility. A bounded Ebers–Moll BJT can next compose sensed conduction-only junctions, CCCS transport and explicitly separate charge-only junctions; it must reject GP base-charge couplings until implemented and qualified. MOS model integration still requires an explicit current/Jacobian and conservative-Q versus legacy Meyer-history decision.

## BSIM3 3.3.0 follow-up

Berkeley publishes the versioned model under its [BSIM3 model catalog](https://bsim.berkeley.edu/models/bsim3/).
The installed ngspice reference is 47+ at corpus/source pin
`e8285746c73a8229cf922487c8ccfcf6bea61bce`. The local
`src/spicelib/devices/bsim3/B3TERMS_OF_USE` identifies acknowledgment
and distribution terms specific to the Berkeley code. Exact files and
notices would need review before redistribution. The ngspice device
directory separates setup/temperature, load and AC routines, but load
still writes simulator state and matrix entries. A reusable interface
must separate trial terminal currents and charge, their Jacobians and
any accepted history from those engine-specific writes.

A native executable probe captured signed drain currents, gm and
gate/drain/bulk charge for NMOS and PMOS. The reported gm magnitudes
matched centered finite differences on a small native sweep. The probe
used omitted junction geometries and ngspice reported perimeter
warnings, so the result is scoped to this reference case. It does not
show that a kernel can be extracted or that charge history agrees
through a switching transient. See the current
[verification record](../evidence/grammar-cohort-checklist.md).

RSpice documents a native implementation and is a technical comparison
point. Its [repository license](https://github.com/JaimeHW/RSpice/blob/29c28a022625fd7cc2c56ead1cce2d3066663074/LICENSE)
restricts reuse. An [OSDI adapter](https://openvaf.github.io/docs/details/osdi/)
offers a wider future path for compact models, subject to exact model
provenance and ABI, state and sparse-stamp qualification. Prefer a
native DC comparison with explicit model versions before integrating
an adapter. Retain AC and transient charge as distinct acceptance gates.

### Executable BSIM3 3.3.0 candidate screen

The native ngspice 3.3.0 and 3.2.4 comparison used identical device
geometry and three gate bias points around signed 0.8 V. At the center
the terminal currents and gm agreed to the retained precision, but NMOS
gate charge was 3.8610991 fC versus 3.8608377 fC. Thevenin's documented
3.2.4 implementation therefore remains a separate candidate/version.
Its terms and numerical scope would need their own review.
[Thevenin device matrix](https://github.com/cramt/thevenin/blob/main/docs/devices.md).

Cogenda VA-BSIM3v3 at `e16cbec5e77bc7bad1d29b8fd392860bdc180a19`
declares CC BY-NC 4.0. The published file also refused compilation in
OpenVAF 23.5.0 because BSIM3toxmGiven was undeclared. Neither a repaired
copy nor this older revision is selected for production.
[Cogenda source and license](https://github.com/cogenda/VA-BSIM3v3/tree/e16cbec5e77bc7bad1d29b8fd392860bdc180a19).

The generated `sp_bsim3v3` source from VACASK declares
AGPL-3.0-or-later and identifies its origin as ngspice
`ngspice-45-84-g7f75f09ec`. The exact downloaded input SHA-256 is
`14e9d752a6db896959f3c97a32112899365e7528221e308250cee9cbc8141f84`.
The OpenVAF Reloaded Windows build `20260616-3-g0e83f1ed` compiled
it to an OSDI 0.4 module with SHA-256
`57dc3bebe54a95b36723b3b1e1b543ba01b32a7c9c9a00524b966b3459161608`.
The older compiler attempt ended in an access violation.
[Generated source](https://raw.githubusercontent.com/robtaylor/VACASK/main/devices/spice/bsim3v3.va)
and [compiler builds](https://fides.fe.uni-lj.si/openvaf/download/).

Ngspice 47+ hosted both the built-in and generated 3.3.0 kernels.
Across three DC points the largest signed drain-current difference was
1.269 pA. Across 25 AC frequencies from 1 kHz to 1 GHz the largest
complex drain-current difference was 1.085e-19 A for a 1 V AC drive.
This is a small comparative kernel probe in the same host. Native
integration into this Rust engine, charge history, general geometry
and model-card coverage remain open. External source and compiled
modules are retained only under the E: experiment directory. Review
exact licensing and select the production kernel before integration.

The larger NMOS check reused the first nested sweep in the ngspice
corpus BSIM330 example, with reporting controls removed. The built-in
and OSDI versions both completed 246 points and shared the checked
drain/gate axes. The maximum absolute drain supply current difference
was 0.506 µA, larger than in the three-point probe. This remains
a measured difference, with no numerical qualification claimed
for the larger grid.

### RSpice design and standalone native evaluation

RSpice commit `29c28a022625fd7cc2c56ead1cce2d3066663074` separates model
parameters, temperature and geometry preprocessing, the intrinsic current and
charge evaluator, and the device stamp with accepted history. Its bulk
BSIM3 3.3.0 implementation is separate from legacy BSIM1/2 and B3SOI
DD/FD/PD. The bulk Rust files inherit RSpice's personal-use license. Use
that split as a design reference and derive the simulator implementation from
appropriately licensed, versioned Berkeley sources.
[RSpice bulk model](https://github.com/JaimeHW/RSpice/tree/29c28a022625fd7cc2c56ead1cce2d3066663074/crates/rspice-core-devices/src/device/mosfet/bsim3v3),
[RSpice license](https://github.com/JaimeHW/RSpice/blob/29c28a022625fd7cc2c56ead1cce2d3066663074/LICENSE),
[Berkeley release catalog](https://bsim.berkeley.edu/models/bsim3/).

A Windows x64 standalone C host loaded the previously built, hash-pinned OSDI
0.4 experiment and called its setup, evaluation, and terminal current and charge
callbacks directly. It evaluated three points each for NMOS and PMOS, with
signed gate bias magnitudes 0.7999, 0.8 and 0.8001 V and signed drain bias
magnitude 0.8 V. Source and bulk were grounded, temperature was 300.15 K,
and the model and geometry were those of the earlier complementary fixture.
With current defined into the device and PMOS gate charge sign normalized
against the ngspice raw observable, maximum differences were
1.269 pA for drain terminal current and 7.89e-31 C for gate charge.
The largest net terminal current and charge residuals were 1.12e-21 A and
3.95e-31 C. This probe uses a generated module for research under its source
license. It is a separate executable and does not connect BSIM3 to the Rust
solver. Production model selection, derivative and transient stamp verification,
dynamic state, and coverage remain open.

The Windows artifacts are in
`E:/Artifacts/Spice-BSIM330-native-probe-20261108T042121361Z`. Build from
that directory after copying the tracked `scripts/Bsim330Probe.c` to `probe.c`, with VS developer PowerShell and
`cl /nologo /W4 /O2 /I <ngspice-osdi-header-directory> probe.c /Fe:probe.exe`.
Run `probe.exe` with the absolute path to the experiment module, after
verifying SHA-256
`57dc3bebe54a95b36723b3b1e1b543ba01b32a7c9c9a00524b966b3459161608`.
The module advertises a 352-byte descriptor and this probe consumes only the
matching prefix of the pinned header layout. `probe.csv` records the terminal
results and `oracle-comparison.json` records differences against the prior
ngspice-47+ built-in BSIM3 3.3.0 raw output. No line or branch coverage,
mutation campaign, or fuzz run was collected for this probe. The ABI negative control rejected Windows version.dll before calling any model functions. StrictDoc 0.30.4 parsed 55 documents with 453 unique IDs and 411 resolved parents.

The same artifact directory contains `corpus-source-audit.csv`, a
per-deck triage index for all 96 decks that did not agree in our 113-deck
replay at `05e2a48`. Each row identifies the local diagnostic, the pinned
RSpice validation contract or saved-table candidate, and a source starting
point. There are 54 saved-table candidates, two locked-grid contracts,
four measure contracts, 14 execution-only smoke contracts, 21 scripted
control contracts and one reference-unsolvable contract. Source paths and
deck paths were checked against the pinned Git tree. A source lead is a
family-level pointer. RSpice runtime pass status remains unverified.

The relevant implementation differences are concrete. RSpice separates
BSIM1/2 level 4/5 parameters and terminal charges from BSIM3. Its SOI
implementations include explicit body currents and topology for DD, FD and
PD. These are 20 of our 96 gaps across the five affected directories.
Its regression harness can extract analysis directives from control blocks
without interpreting the full script. It separately runs named pole-zero and
sensitivity analyses, and has an accepted-step-grid replay path for
transient references. These explain why a RSpice contract can be satisfied
while our original requested-analysis or stricter waveform comparison remains
open. The resistance array already matched 51/51 rows on an aligned grid.
The RC deck is an RSpice smoke test and our aligned-grid source-current
disagreement remains open.
[RSpice validation runner](https://github.com/JaimeHW/RSpice/tree/29c28a022625fd7cc2c56ead1cce2d3066663074/tools/rspice-conformance/src/suites/ngspice).

### Observed RSpice baseline

A Windows x64 debug build of RSpice at
`29c28a022625fd7cc2c56ead1cce2d3066663074` ran its ngspice case runner
on all 113 shared decks. The build used Rust 1.99.1 MSVC and
`cargo build --locked -j 4 -p rspice-conformance --no-default-features --features circuit-suites --bin rspice-ngspice-case-runner`.
This differs from the upstream release/nightly configuration. Each case had
a 30-second soft limit and a 33-second process watchdog, with at most three
cases active. The shared RSpice run used its checked-in references.
Our replay used live ngspice-47+ with the existing requested-analysis
comparator, frontend compiled at `543ed67` and the frozen engine from the
prior replay. Checkout `d2faf1e` added only the standalone probe and
documentation after that frontend build. The per-deck join verified all 113
root input hashes match. Reference observables, tolerances, time grids,
control effects, and acceptance criteria differ across the two runners.

| Observed result | Count |
| --- | ---: |
| RSpice reference contracts passed | 67 |
| RSpice scripted-control contracts passed | 23 |
| RSpice smoke contracts passed | 18 |
| RSpice reference-unsolvable contract satisfied | 1 |
| RSpice numerical mismatches | 2 |
| RSpice solver deadline or process watchdog | 2 |
| Our requested analyses executed | 19 |
| Our requested analyses agreed | 17 |

Of the 67 RSpice reference passes, our comparator agreed on nine, disagreed
on one, and refused preparation on 57. The 58 remaining reference gaps
include 15 BSIM3 SOI decks, seven MESA decks, six pole-zero decks, five
transmission-line decks and five VBIC decks. Smoke and scripted-control
results are recorded separately. The four RSpice failures were
`hfet/inverter.cir` and `transmission/txl2_3_line.cir` (mismatches),
`bsim3soidd/ring51.cir` (solver deadline), and
`bsim3soipd/ring51.cir` (watchdog).

The shared inventory contains no bulk BSIM3 3.3.0 deck. A separate,
246-point ngspice nested DC fixture with an explicit
`.print dc v(22) i(vds)` passed RSpice's live-reference comparison for those
observables. The same input bytes on our simulator reached
`bsim3_kernel_unavailable`. RSpice also passed against a saved two-observable
table for the original reporting-free variant and failed a negative control
whose reference drain-source supply current was replaced with 1 A. That
failure confirms the saved comparison used the current column. The
live-reference run needed the print directive because a batch run without
it emitted no comparable table. RSpice's Windows filename check also
required a copy of the same verified console ngspice binary named
`ngspice_con.exe`. These runs establish a bulk BSIM3 DC gap. AC and transient
BSIM3 parity remain unmeasured.

Raw results, `verified-matrix.csv`, `observed-gaps.csv`, both bulk runs,
and the local replay manifest are under
`E:/Artifacts/Spice-RSpice-parity-20261108T054942Z`. The matrix SHA-256 is
`8ffcd24188296f9c2834b0253ed1eef6af3357841001a50d0333c11b713da51a`.
The 91-row gap file links observed reference, smoke and scripted-control
gaps to source leads. Its SHA-256 is
`1aa4b97e0a2bcd29012d0e4a2f64077ad6a8a8907204f4a6c75334b49c812956`.
A family-level source lead is a starting point for implementation. There
were no product code changes or new coverage measurements in this baseline.

### Opt-in bulk BSIM3 3.3.0 execution

The Windows x64 reference engine accepts the bounded bulk model on its
normal operating-point and DC sweep paths. The frontend binds level 8 or
49 with explicit version 3.3.0, TOX, NCH, NSUB, VTH0, W and L. The optional
PD, PS, AD and AS default to zero. Geometry uses SI units, and NCH/NSUB
use the SPICE card convention of inverse cubic centimetres. The provider
evaluates at 27 Celsius with the generated module's model defaults and
1 pS junction Gmin. The D/G/S/B currents and centered numerical current
derivatives are stamped directly into the Rust Newton system. Four-terminal
DC KCL and power checks include the device currents. Other model terms
and uncollapsed internal unknowns are outside this provider slice.

For the existing development fixture, verify the OSDI module's SHA-256
recorded above. Using a Visual Studio developer shell from a checked
artifact directory, compile the tracked adapter with
`cl /nologo /W4 /O2 /LD /I <pinned-ngspice-osdi-headers> <checkout>/scripts/Bsim330Host.c /Fe:host.dll`.
Set `SPICE_BSIM330_HOST` to the absolute adapter DLL path and
`SPICE_BSIM330_MODEL` to the absolute verified OSDI module path before starting
the normal frontend or engine. Both files are executable native code and
must come from trusted local builds. Deck input cannot choose either path.
The generated external module and the ngspice OSDI headers are governed by
their respective licenses. The repository tracks the adapter and ships
neither external file. This remains an opt-in provider with an external
model dependency. Non-Windows systems and installations without the native
files cannot evaluate this model.

Verification artifacts are under
`E:/Artifacts/Spice-bsim3-20261108T112514Z`. The final
`final-sweep/summary.json` verified the same 246-point nested fixture used
in the RSpice live run with SHA-256
`466b3114ebd4cbf4e4a467ad76df2fe900a6b7b14954a654bc24d666dbcf9869`.
All 11 mapped observables agreed at every point with ngspice-47+.
The maximum errors on v(22) and i(vds) were 1.658 microvolts and
1.658 nanoamperes. A complementary PMOS operating point through the Rust
CLI matched ngspice drain supply current within 0.705 picoamperes.
The shared 113-deck replay still had 19 executed and 17 agreed. The bulk
fixture is additional and outside that inventory.

Run the targeted Rust checks with
`cargo test --locked --test bsim330 -- --include-ignored` after configuring
the provider. Both tests passed, including 64 bias cases with Proptest seed
330. A C adapter mutant that inverted drain current was killed by the
native stamp test. The public CLI rejected absent host configuration and
unsupported AC on the DC-only checkpoint. The frontend suite had 698 passed and 37 opt-in skips.
Coverlet reported 94.32% line and 83.68% branch coverage for
Simulator.Frontend. Rust, C, and generated module coverage were not measured.
StrictDoc 0.30.4 parsed 55 documents, 454 unique IDs and 412 resolved parents.
The native host used for the replay had SHA-256
`e2daae9246a498ef3a753e9364356c01cd8721fd5c49d9e6ec4160ef13f23e56`.

### BSIM3 AC and backward Euler

AC uses the converged DC bias and stamps the current and charge Jacobians
as G + jωC. Backward Euler evaluates the signed terminal charges from the
trial and accepted voltage states, divides their difference by the
timestep, and includes the trial charge derivative in Newton. The model
host has no mutable per-step history. A rejected adaptive candidate leaves
the prior state intact. Fixed-step KCL includes the reconstructed model
current and displacement current. Independent nonlinear energy and
incremental AC physical audits are unavailable. Nonlinear trapezoidal
integration is outside this profile.

The three ngspice fixtures in compat/ngspice are reproducible through the
existing Run-RequestedCorpus driver. Build the Rust engine with
the provider configuration above, prepare a hash-pinned inventory of
these decks, and compare against ngspice-47+ using the normal requested
analysis path. Both original dynamic fixtures refused on the DC-only
checkpoint. On the updated engine:

| Fixture | Compared points | Observables | Failures |
| --- | ---: | ---: | ---: |
| bsim330-ac.cir | 49 | 8 | 0 |
| bsim330-tran.cir | 201 | 4 | 0 |
| bsim330-loaded-tran.cir | 1001 | 5 | 0 |

The AC case includes NMOS and PMOS with different gate phasors. Its
maximum gate-current error was 37.8 fA. The fixed drain gate ramp had
a maximum gate-current error of 2.91 pA. In the loaded transient, the
drain voltage is an unknown with resistive loading and external storage.
Its maximum drain voltage and gate current errors were 0.382 µV and
17.8 pA. The adaptive loaded request finished with 76 accepted and four
rejected steps. This was a separate execution check, without a full
adaptive waveform comparison to ngspice.

The three targeted native tests passed, including 64 seeded Proptest
bias checks (seed 330). The zero-terminal-charge adapter mutation was
killed by the dynamic stamp test. The related audit, nonlinear, and
sparse AC suites passed 49 tests. Instrumented targeted tests measured
95.59% line coverage for src/bsim330.rs and 15.04% for the entire
src/newton.rs. The measured scope excludes branch coverage and the
external C host and OSDI model. StrictDoc 0.30.4 parsed 55 documents,
455 unique IDs and 413 resolved parents. Raw results, coverage and
mutation evidence are under E:/Artifacts/Spice-bsim3-dynamic-20261108.

The 113-deck shared corpus replay retained 19 executed and 17 agreed.
All three new dynamic fixtures are outside that inventory. The debug
engine SHA-256 was
96ca8b0ff7f4091f32867eadde25d6e34583351cdda220d26f36c1778a439b65.
The targeted Rust command was cargo test --offline --locked --test
bsim330 -- --include-ignored. The two native replay manifests are
dynamic-replay/summary.json and loaded-replay/summary.json. They record
source hashes, reference tool version, tolerances and per-signal counts.

### BSIM3 SOI native device trial

The 15 observed RSpice reference passes include 12 DC decks and three gate-ramp transients. The FD ring is a separate smoke contract.
The three families require distinct kernels. FD is level 55, DD is level
56, and PD is level 57. The observed decks use numerical option cards and
resolved model includes. Those options currently fail preparation before
device binding. A four-node MOS card names the back gate as the fourth
terminal. Its treatment of a floating body depends on the level. The
DD and PD t3 cards allocate a separate internal body unknown.

The standalone experiment used Berkeley BSIM Research Group model code
from ngspice commit e8285746c73a8229cf922487c8ccfcf6bea61bce, with the
individual BsimTerms_use conditions in each model directory. It compiled
the model load, setup, parameter and temperature objects without linking
the ngspice analysis engine. A small local DC matrix and RHS captured the
stamps. The probe recovered signed terminal current as Jv minus the
linearized RHS. The existing ngspice executable was used independently as
the full-circuit oracle. RSpice source was not incorporated.

| t3 device | Bias points | Points outside tolerance | Maximum terminal-current error |
| --- | ---: | ---: | ---: |
| FD | 1806 | 0 | 8.67e-18 A |
| DD | 1806 | 860 | 25.10 µA |
| PD | 1806 | 72 | 0.326 µA |

The test compared drain, gate, source and back-gate currents, using an
absolute tolerance of 1 nA plus 1e-4 times the reference magnitude.
The FD maximum terminal KCL residual was 1.74e-18 A. The local floating-body
solve for DD and PD returned finite values at all biases, but current
disagreements remain. A small body residual does not establish agreement
with ngspice's continuation path. No normal simulator deck was enabled
by this trial.

The FD harness is scripts/Bsim3SoiFdProbe.c. To reproduce, use VS
developer PowerShell in a checked artifact directory with the pinned
ngspice checkout. Generate fd-card.inc as an array of name/value entries
from the pinned bsim3soifd/nmosfd.mod, omitting the LEVEL selector. Its
verified SHA-256 is
97130d27d2de147dda7c7cb3cb4366e89cd58b544d4a0d08b02bf8e02667df20.
Compile b3soifd, b3soifdset, b3soifdtemp, b3soifdcheck, b3soifdmpar
and b3soifdld from src/spicelib/devices/bsim3soi_fd with MSVC /O2.
Include visualc/src/include, src/include, the device directory and the
artifact directory. Link these objects with the harness. The tracked
source reproduced the complete FD trial byte for byte. An unknown
model field was rejected in a separate compile-and-run negative check.
No line or branch coverage or randomized campaign was collected for
this standalone harness. Artifacts are under
E:/Artifacts/Spice-soi-20261108T115024Z.

### Linux bulk provider and typed FD operating point

The bulk BSIM3 3.3.0 adapter now builds on Windows x64 and Linux x86-64.
Linux uses an ELF adapter and ELF OSDI module. The Windows DLL and
PE OSDI module cannot be loaded there. In a checked Linux build directory,
compile scripts/Bsim330Host.c as a position-independent shared library
with the pinned OSDI include directory and libdl. Set SPICE_BSIM330_HOST
and SPICE_BSIM330_MODEL to its absolute path and that of the trusted ELF
model. The tested module used the same Verilog-A source and OpenVAF
Reloaded 0.4 release as the Windows experiment. The Windows OpenVAF
executable generated ELF objects using target x86_64-unknown-linux. They
were linked with the Linux C toolchain into the tested shared module.

The separate SPICE_BSIM_SOI_FD_HOST setting points to an FD native
library. Build scripts/Bsim3SoiFdProbe.c and the same six Berkeley model
objects described above as a shared library, defining SPICE_SOI_FD_HOST
for the adapter. Keep the pinned fd-card.inc in the include path. On Linux, compile upstream objects as position-independent code and
force-include scripts/Bsim3SoiFdStdio.h in the six device objects so
model diagnostics cannot enter the request protocol stream. Link with libm and
bind the shared library's internal symbols locally.

The typed request selector bsim_soi_fd_reference accepts id, p, n, gate
and back_gate. It uses the pinned FD NMOS card, 10 µm width, 0.25 µm
length, 27 °C and 1e-25 S model Gmin. It does not represent arbitrary
model cards. The Rust solver stamps D/G/S/E currents and their
derivatives and audits all four terminals. The legacy device workspace
is serialized per process, and unavailable charge is represented
explicitly. AC and transient refuse this profile. Original SOI netlist
binding and options, DD and PD, and all 15 corpus acceptance gates
remain open.

On Ubuntu 24.04 in WSL, the four opt-in Rust checks passed, including
the existing 64 seeded bulk bias cases, AC charge and backward-Euler
history, the typed FD operating point and the unavailable FD charge
check. A CLI test verified a single JSON response with model diagnostics
on stderr. The FD drain supply current at VD=VG=0.8 V, VS=0 V and VE=-1 V
matched live ngspice within 1 pA. The missing-host request refused and
the FD current-sign mutant was killed. The .NET client built without
warnings. Targeted Rust line coverage was 90.83% in src/bsim330.rs
and 46.78% in the whole src/newton.rs. Branch and native C coverage were
unmeasured. StrictDoc parsed 55 documents and 457 unique requirement
IDs. Artifacts and provider hashes are under
E:/Artifacts/Spice-bsim-linux-20261108.

The same four tests and single-response back-gate CLI check passed on
Windows x64. Both CLI comparisons differed from ngspice by 1.63e-19 A
at the reported bias. Raw OSDI modules, native libraries and model
fixtures remain external build inputs.
