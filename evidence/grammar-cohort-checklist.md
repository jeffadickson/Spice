# Grammar to original-deck checklist

Count original paths and distinct expanded input hashes separately. For each
cohort, record the generated production, executable semantic profile,
original requested analysis, native comparison and any remaining reporting
checks. A derivative is diagnostic evidence. Its results belong to a separate
row from the original input.

| Original cohort | Generated productions | Execution work | Numerical or reporting evidence |
|---|---|---|---|
| tests/regression/parser/minus-minus.cir | controlDefine, controlOp, controlLet, controlIf, controlEcho, controlQuit | One pure prefix definition, function relations and local vector aliasing | Diagnostic OP agreed exactly on four observables. Native original printed both node voltages and the reporting vector as 5. |
| examples/control_structs/if-batchmode.cir and paranoia_parallel mirror | controlIf, controlBoundary, controlEcho, controlTran, controlReport; inactive unset is controlUnknown | Batch condition must select the ordinary root transient and keep the interactive branch inactive | Two paths share one exact expanded hash. Baseline accepts syntax and prepares the physical circuit. Both original requested plans refuse conditional_before_last_analysis. |
| tests/regression/lib-processing/scope-1.cir, scope-2.cir and scope-3.cir | controlDefine, controlOp, controlLet, controlIf; foreach requires a separate command profile | Prefix function plus bounded vector construction, loop dispatch and local substitution | Historical preparation reached all three physical circuits. The complete reporting programs need separate verification. |

## Batch-mode RC diagnostic

Both unchanged native decks ran in batch mode, selected the batch branch and
produced identical numeric tables. Removing the control block from a
diagnostic copy retained the same native table, including all 111 samples.
The engine executed the derivative. Across its 100 positive samples, 14
output-voltage values and 54 source-current values missed the existing
comparison bounds. The maximum output-voltage discrepancy was 287.98 uV.

Using the exact RC response V(t)=1-exp(-t/1 ms), the engine's maximum
voltage error was 306.90 uV. The linearly interpolated native table
differed from the same solution by up to 201.31 uV and would reject the
analytic waveform on 28 voltage and 85 source-current comparisons at the
engine sample times. This establishes two independent qualifications:
batch dispatch and transient accuracy/reference sampling.

The diagnostic work used base 8f5cbca and ngspice-47+ on Windows.
The two original inputs both have SHA-256
b0769832f69df561c9d40f5387394f8bfef7b2b0f5cbe2368d200708daa52bc5.
E:/Artifacts/Spice-grammar-cohort-8f5cbca contains the baseline, original
native raw tables, deck-only probe, and analytic-audit.json.

## Original reporting-function qualification

The final prefix replay covered the 12 historically circuit-prepared paths
with unsupported_control_postprocessing_before_analysis. All 12 input and
expanded hashes matched the baseline, representing 11 distinct expanded
inputs. Original executions and native agreements both went from zero to
one. tests/regression/parser/minus-minus.cir matched all four operating-point
observables exactly. Its separate reporting result reproduced all five
deck-emitted native lines, exited zero and emitted no diagnostics.

The regression first failed at prefix admission. The intermediate replay
then revealed that BODY lexing preserves a-b as one hyphenated name.
Arithmetic-mode retokenization applies only to an otherwise unresolved
bounded subtraction of known function formals. The grammar registry now
records bounded define and let execution. Remaining replay diagnostics
were six unsafe control-token refusals, three earlier declaration refusals,
and two unsupported prefix-effect refusals.

The final prefix replay tested 8f5cbca plus the patch and had valid
provenance. It took 13.39 seconds. Under the same E: artifact root,
prefix-cohort-inventory.json SHA-256 is
c7c485a2b3d1fa7d53fc41b80af12345ab5bab951f28f463fe4a419ebb21ee63
and prefix-cohort/summary.json SHA-256 is
f77cab44e894de88edbaaa1976e86b8ca58572f38041c56a2694962377416f45.
Use the documented Run-RequestedCorpus.cs invocation with the pinned native
engine, corpus and inventory to reproduce it.

Verification passed 591 frontend tests with 37 opt-in skips and 471
syntax tests with one symlink skip. StrictDoc parsed 52 documents, 425
unique IDs and 393 resolved parents. The regenerated grammar map assessed
94 of 97 generated rules. Coverlet measured 77.54% line and 60.57% branch
coverage across managed assemblies. A deliberate plus-for-minus mutation
changed the reporting exit from zero to one and was caught. The frontend
suite included its existing seed-based properties. Standalone fuzzing and
Rust coverage were outside this checkpoint.

## Original terminal foreach cohort

The six paths previously refused on unsafe control tokens form a shared
reporting profile. Generated controlForeach retains the loop header,
controlSet retains the node-name templates, and controlLet/controlIf consume
the resulting named observables. All six original paths have distinct
expanded hashes. Each matches its previous replay identity and expansion.

| Original deck | Operating point | Native report checks |
|---|---|---:|
| tests/regression/lib-processing/scope-1.cir | Agreed | 2 |
| tests/regression/lib-processing/scope-2.cir | Agreed | 3 |
| tests/regression/lib-processing/scope-3.cir | Agreed | 3 |
| tests/regression/misc/bugs-1.cir | Agreed | 8 |
| tests/regression/subckt-processing/global-1.cir | Agreed | 3 |
| tests/regression/subckt-processing/model-scope-5.cir | Agreed | 7 |

Original executions and native operating-point agreements both rose from
zero to six in this cohort. The independent unchanged ngspice-47+ runs
and our separate reporting runs emitted the same six success lines,
covering 26 assertions, with exit zero and no reporting diagnostics.
The projected comparisons covered 89 original voltage and branch-current
observables. The native numeric projections explicitly omit reporting
commands while retaining the original analysis. These are six original
results rather than a fresh full-corpus count.

Windows artifacts are under E:/Artifacts/Spice-foreach-2668bf1.
The final run tested base 2668bf1 plus the patch with the existing issue40
engine and pinned corpus e8285746c73a8229cf922487c8ccfcf6bea61bce. It finished
in 9.46 seconds with valid provenance. The inventory SHA-256 is
48bb666668161e45c36180bb41f785dfe2a4343364aa7df89ba1ca0b1b60e8cc.
The summary SHA-256 is
cf2fb5d7df8bab0cc243a010bb8f1e4218756c3ba8efb8a8a1efd31c5e2f5fda.
Use the documented Run-RequestedCorpus.cs invocation with inventory.json.
The six *.native.log files retain the separate unchanged reporting runs.

Verification passed 598 frontend tests with 37 opt-in skips and 471
syntax tests with one symlink skip. StrictDoc parsed 52 documents and
426 unique IDs. The regenerated grammar map assessed 95 of 98 rules.
Managed coverage measured 77.45% line and 60.35% branch. The new FsCheck
loop-count property requested 100 cases with seed 2668,20261107. A
skip-every-other-item mutant failed both the property and the two-item
report. FsCheck found input NonNegativeInt 1 and saved the failing replay
15956658760579799124,10030303121570407149,3. Negative tests also exercise
oversized loops, missing observables, physical commands and nesting.
Rust coverage and a standalone fuzz campaign were outside this checkpoint.

## Short PULSE transient

The original tests/general/rc.cir now completes its requested transient.
This adds one original execution and zero complete ngspice agreements in
the targeted replay. The generated grammar already accepted the deck.
FRONTEND-PULSE-HORIZON-001 covers preparation of the first rise and hold
when width and period default to the requested stop time.

| Observable | Compared points | Outside tolerance | Maximum absolute error |
|---|---:|---:|---:|
| Source voltage v(1) | 71 | 0 | 2.22e-16 V |
| Capacitor voltage v(2) | 71 | 70 | 0.04260 V |
| Source current i(vin) | 71 | 70 | 0.04260 A |

The next RC investigation is the transient integration method and time grid.
The full corpus was outside this replay.

| Remaining original control paths | Behavior needed after parsing |
|---|---|
| examples/various/atanh.sp | Vector composition, scale selection, complex atanh and polar reporting |
| Both param_sweep.cir copies | Bounded while, resistor alteration, repeated run and plot/output state |
| Both MonteCarlo.sp copies | Plot state, dynamic substitutions, stochastic functions and replayable seeds, alteration, repeated AC and measurements |

The final Run-RequestedCorpus.cs replay used base 874e8e6 plus the patch,
the existing issue40 Rust engine and corpus
e8285746c73a8229cf922487c8ccfcf6bea61bce. It reported valid provenance.
Artifacts are under E:/Artifacts/Spice-short-pulse-874e8e6. The inventory
SHA-256 is 8cb95a49f540792ff680edc889d56afdf0e6a7477d24617e760b55c8c6175e87.
The final/summary.json SHA-256 is
6e8c3ffcba6cd428fcb31b8a5a004fed56f5e7dd5b8d86ba43a3ed9f0ece1e64.
The independent unchanged ngspice execution is retained in rc-original.log
and rc-original.raw.

The frontend suite passed 600 tests with 37 opt-in skips. Managed Coverlet
coverage was 77.46% line and 60.37% branch. StrictDoc parsed 52 documents
with 428 unique IDs and 396 resolved parents. Both short-pulse assertions
failed before the fix and also caught a deliberate doubled-rise mutant.
The existing frontend suite includes seeded properties. Rust coverage and
standalone fuzzing were outside this checkpoint.

### RC transient discrepancy diagnosis

At the end of the 0.1 s source ramp, the exact 1-ohm, 1-farad response
is 0.04837418036 V. The configured request advances from 0 to 0.1 s in
one backward-Euler step. Evaluating the input at the right endpoint
gives (0 + 0.1 * 1) / (1 + 0.1) = 0.09090909091 V, exactly the Rust
result. ngspice starts at 0.0007 s and gives 0.04830771117 V at 0.1 s.
The source current follows i(vin) = (v(2) - v(1)) / 1 ohm, so the
capacitor voltage error explains both discrepancies. The engine
satisfies that relation within 1.12e-16 A in the trials below.

Replaying the same serialized request through the same Rust executable,
with only integration controls varied, gave these maximum capacitor
voltage errors against the exact solution on each run's own time grid.

| Integration | Accepted steps | Maximum error |
|---|---:|---:|
| Baseline backward Euler, 0.1 s ceiling | 70 | 0.042535 V |
| Trapezoidal, same ceiling | 70 | 0.00075513 V |
| Backward Euler, 0.001 s ceiling | 7000 | 0.00045213 V |
| Trapezoidal, 0.001 s ceiling | 7000 | 0.00000007540 V |
| Trapezoidal with local error control, 0.1 s ceiling | 449 | 0.000001282 V |
| Original ngspice trace | 85 | 0.00024156 V |

The analytical voltage during the ramp is 10 * (t - 1 + exp(-t))
in volts with t in seconds. After 0.1 s, the remaining deficit from
1 V decays with the 1 s time constant. The method-only replay confirms
that the dominant error is the coarse right-endpoint approximation
during the ramp. The refined and adaptive replays demonstrate numerical
convergence on this circuit. They are analytical diagnostics. The
original strict ngspice comparison remains unqualified, and the
native trace has measurable integration error of its own.

The adaptive trial used relative tolerance 1e-7, absolute voltage and
current tolerances 1e-9, minimum step 1e-7 s and 40000 attempts. It
rejected six steps. The unchanged request and all diagnostic responses
are under E:/Artifacts/Spice-short-pulse-874e8e6/rc-root-cause.
The audit.json SHA-256 is
1b1a3ae271bf297e6f77a35ddb63c82e0c9beeec67b101aa683f4b0d5c382286.
The reused Rust engine SHA-256 is
037406562cd2518633d4d05debeceb3c5cf3252f16a52f4b68d9bbdde318b4cce.
This investigation tested a2811d2 artifacts and made no solver or
comparison policy change.

### Adaptive trapezoidal checkpoint

FRONTEND-TRAN-DRIVEN-LTE-001 applies the existing Rust step-doubling
controller to requested, waveform-driven linear storage transients
with ordinary operating-point startup. The default and explicit
second-order trapezoidal settings use it. Explicit first-order
settings, UIC, clamped IC and nonlinear startup retain their prior
policies. The Rust executable and comparison tolerances are unchanged.

The original tests/general/rc.cir accepted 449 steps and rejected six.
The exact RC peak voltage and signed source-current errors are
1.28211e-6 V and 1.28211e-6 A. The strict native replay remains
unqualified. Of 450 points, zero source voltage, 180 capacitor voltage
and 429 source current comparisons exceed their bounds. The peak native
difference is 1.71971e-3 V and A at 0.07971864 s. Interpolating the exact
solution between the two bracketing ngspice points alone introduces
1.76315e-3 V there. The ngspice endpoint errors contribute -4.35017e-5 V
after interpolation and our trace differs from exact by -5.66179e-8 V
there. This separates the output-grid effect from integration error.

The 19 previously executable transient decks selected from the prior
928-row report retained their row classifications. Six agreed on all
requested analyses and 13 remained partially qualified. The original RC
deck is additional to that cohort. Both reports had valid provenance.
Artifacts are under E:/Artifacts/Spice-adaptive-trap-ea14ede.
SHA-256 for original/summary.json is
dede4dccdb6b0b824a8f2d313d6e7b49aad9013347f052f87f39c06e148c2159.
SHA-256 for transient-cohort/summary.json is
5292b6023b3dab1c01ea7e79efe53e27a9d99b468ed1407e720afd144c375f58.
SHA-256 for analytic-audit.json is
7731c763999a1b646771d76a8c67bcbd8008d40e3c042ba269eaf74c11cc0b43.

Frontend verification passed 603 tests with 37 opt-in skips, including
the existing seeded properties in the executed suite. The new default
and explicit trapezoidal cases failed before the fix and both caught a
mutant that cut the attempts allowance from four times the accepted-step
ceiling to one. Managed Coverlet reported 77.47% line and 60.45% branch
coverage. StrictDoc parsed 52 documents with 429 IDs and 397 resolved
parents. Rust coverage, standalone fuzzing and a full census were
outside this checkpoint.

## Scalar resistor AC override

The original tests/resistance/res_partition.cir and repository
compat/ngspice/res_partition.cir previously refused the AC resistance
assignment. Both now execute and agree with ngspice on their requested
OP and all 41 AC frequencies. Each comparison covers two voltages and
the signed source current. The expected output divider ratio is 0.5
at DC and 0.75 at AC. The maximum AC current difference was 6.78e-21 A.
FRONTEND-RESISTOR-AC-001 traces the bounded linear profile. Resistor
model geometry, multiplicity and scaling combinations in res_array.cir
remain separate work. Bias-dependent AC still needs separate DC and AC
conductance stamps.

The current engine also completed the four previously failed transistor
amplifier transients, comprising spice3.deck and xspice_c1.cir and
their parallel copies. All four still have numeric discrepancies.
The largest voltage difference was 1.389 mV at the collector. This
replay did not reproduce the historical LU refinement failure and does
not assign a cause or count the four decks as native agreements.

The two bounded Run-RequestedCorpus.cs reports have valid provenance
and are under E:/Artifacts/Spice-blockers-fab8472. The transistor replay
tested fab8472 before the resistor patch, and the resistor replay tested
fab8472 plus the patch. The corpus pin is
e8285746c73a8229cf922487c8ccfcf6bea61bce. SHA-256 for
resistor-replay/summary.json is
2987edea5e2791904b2c04f7177cbe9120e1739a2541eeb2d9403f8d67c7c4a4.
SHA-256 for bjt-replay/summary.json is
f89c7f4169ceaa1dd2b28673522190cd9aa5f334b443c833bf6e37b470b475e7.
The 928-row census was not repeated.

Frontend verification passed 606 tests with 37 opt-in skips. Syntax
verification passed 471 tests with one symlink skip. StrictDoc parsed
52 documents, 430 unique IDs and 398 resolved parents. Coverlet measured
77.48% line and 60.65% branch coverage across the managed frontend test
scope. The positive test failed before implementation and caught a
deliberate mutant that reused the DC resistance in AC. The frontend
suite ran its existing seeded properties. Rust coverage and standalone
fuzzing were outside this checkpoint.

## Original resistor array

The original tests/resistance/res_array.cir now completes its requested
OP, 201-point AC sweep and 51-point transient. OP and AC agree with
ngspice on all compared observables. The greatest AC current difference
was 2.17e-19 A. The native R3 sheet width is exactly zero after narrowing,
so i(vr3) is 0 A in OP and 400 µA at unit AC excitation. The explicit AC
resistance is applied only in small-signal analysis.

The unmodified transient comparison still flags a 25.329 mV source
difference at 1 ns. The ngspice samples bracketing the delayed sine
onset are at 0.856 ns and 1.056 ns. Linear interpolation between them
gives 25.329 mV at the onset, where the exact value and the engine value
are both zero. Using V(t) = sin(2 pi 1e8 max(0,t - 1e-9)) and
i(Vin) = -0.0005 V(t) A, each trace agrees on its own sample grid within
3.56e-15 V and 1.79e-18 A. This is analytical evidence for the source and
source current, separate from strict paired trace qualification.
The comparison policy and tolerances are unchanged.

The 24-row replay of previously resistor- or source-bias-blocked
inputs completed with valid provenance. Both previously fixed divider
copies still agree, the array newly executes, and 21 remain blocked.
Relative to the preceding checkpoint this is one new deck executing
and zero new decks fully agreeing under the existing comparator.
No full corpus rerun was performed.

FRONTEND-RESISTOR-ARRAY-001 traces the requested instance options and
bounded memoryless SIN startup. Frontend tests passed 613 with 37
opt-in skips. Syntax tests passed 471 with one symlink skip. The
positive test failed before implementation and caught a mutant
multiplying the AC resistance by M. The suite ran its existing
seeded properties. Managed coverage was 77.49% line and 60.93% branch.
StrictDoc parsed 52 documents, 433 unique IDs and 401 parents. Rust
coverage and standalone fuzzing were outside this checkpoint.

The final replay tested base 8ca32b0 plus the patch on corpus
e8285746c73a8229cf922487c8ccfcf6bea61bce. Artifacts are under
E:/Artifacts/Spice-res-array-8ca32b0. The final-replay/summary.json
SHA-256 is ac6f28b31d4f8ca0644bc9c1bc5f37b11d906e3e4056767a71d477832e5b4842.
The sine-audit.json SHA-256 is
6d1f503df95c41ba101651b134c8e9ef8c5c4ae4ce15f636ba2798e8740522da.

## Native-default pulse count

The two arbitrary_phase.deck paths now run the original 2 ms transient.
They are byte-identical, so this is two newly executable corpus paths
covering one distinct circuit. In default ngspice mode the last PULSE
field is a count. The positive count 45 leaves the full requested window
periodic. The actual native startup is 0.707106781 V for the phased sine
and -1 V for the pulse. Neither original deck was edited. The eight-field
cutoff inside a requested horizon remains explicit, as does the separate
XSPICE phase dialect.

Pulse voltage and signed source current agree at all 101 compared points.
Sine voltage and signed source current each fail 91 strict comparisons,
with peaks 1.972 mV and 1.972 µA after native interpolation. On their own
grids the 101 engine and 136 native samples match
sin(2 pi 1000 t + pi/4) within 3.50e-15 V. The 1 ms periodic pulse
with 10 µs edges and 500 µs high time matches within 1.12e-16 V.
These analytical checks leave the strict whole-deck classification
unqualified. The previous resistor array retained its classification.
The two counter decks still refuse XSPICE models and control paths.

FRONTEND-PULSE-COUNT-001 links the boundary tests and the native replay.
The cutoff comparison mutant changed strict greater-than to greater-than
or equal and failed the exact TSTOP case. The new positive cases failed
before implementation. The malformed waveform test now uses nine fields.
StrictDoc parsed 52 documents, 434 unique IDs and 402 resolved parents.

The replay tested base cc5aeb9 plus the implementation with valid
provenance and corpus e8285746c73a8229cf922487c8ccfcf6bea61bce.
Artifacts are under E:/Artifacts/Spice-pulse-count-cc5aeb9. The replay
summary SHA-256 is 401f572b2bf8c5e8c015f9b139ec3d1d263d9e8c50f58518aedd13c27733f735.
The analytical audit SHA-256 is
484ef96186580e8e03a209f4297de571764a0b241da2bce6af99ae7223a1ccfb.
The full corpus, standalone fuzzing and Rust coverage were outside this
checkpoint.

The final frontend suite passed 617 tests with 37 opt-in skips and
included the existing seeded properties. Managed line coverage was
77.49% and branch coverage was 60.96%.
