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

## Own-grid analytical reference

SEM-TRANSIENT-MEMORYLESS-001 adds supplemental analytical evidence for
the exact expanded hashes of the two distinct memoryless circuits below.
It checks every mapped voltage and signed branch current against the
closed form at each engine and native sample time. The strict comparison
and its existing classification are independent of this result.

| Original entries | Observables per entry | Engine / native samples | Maximum voltage error | Maximum current error |
|---|---:|---:|---:|---:|
| Both arbitrary_phase.deck paths | 4 | 101 / 136 | 3.50e-15 V | 3.53e-18 A |
| tests/resistance/res_array.cir | 12 | 51 / 59 | 7.78e-16 V | 4.34e-19 A |

All 3216 analytical comparisons passed across the three original entries.
All three strict transient comparisons retained their interpolation-driven
disagreement. The array OP and AC retained strict agreement.

The replay tested base 6587a6d plus the implementation with valid source
provenance and corpus e8285746c73a8229cf922487c8ccfcf6bea61bce.
The report is under E:/Artifacts/Spice-memoryless-6587a6d/replay.
Its summary.json SHA-256 is
d65a80e9e05e255155fdbc84ddd6816e16c0e1edce6e850a777c2a5b35c510f0.
The exact verified source and test snapshot are in the sibling verified
directory because C: reached zero free bytes.

The frontend suite passed 622 tests with 37 opt-in skips. Managed
coverage was 77.53% line and 61.11% branch. The reference implementation
class reached 87.93% line and 82.14% branch. Tests cover mismatched grids,
corrupt signed current on each side, missing probes and unknown hashes.
A reversed reference-current sign was caught by the focused tests.
The frontend suite ran its existing seeded properties. StrictDoc parsed
52 documents, 435 unique IDs and 403 resolved parents. Rust coverage,
standalone fuzzing and a full corpus replay were outside this checkpoint.

## Controlled PWL startup and XSPICE current gain

Two distinct preparation blockers were cleared in a matched five-path
replay. Baseline 65a411f refused all five paths. With the patch, three
paths execute and one fully agrees under the existing comparator.

| Original paths | Current outcome |
|---|---|
| examples/probe/cntrl-sources.cir | Requested transient agreed on all 12 mapped voltages and signed branch currents |
| Both polarity.deck copies | Requested transient completed and strict interpolation comparison still differs |
| Both mixed_ref.deck copies | Gain current ports are admitted and the next refusal is the unsupported XSPICE model |

Native startup for cntrl-sources.cir applies the 0.9 V PWL waveform at
zero time even with a distinct explicit DC value. The engine now uses
that same startup for the memoryless circuit. Maximum compared voltage
error was 8.89e-16 V.

For polarity.deck let s = sin(2 pi 1000 t). The five node voltages in
node order 1, 10, 2, 11, 12 are s, 10s, 0, .01s and -10000s V.
The signed currents i(v1), i(a1) and i(a2) are -.002s, -.01s and
-.00001s A. The own-grid audit of all eight mapped observables found
maximum errors of 9.10e-12 V and 9.11e-18 A across engine and ngspice.
The strict comparison interpolates between native samples and differs by
up to 3.98 V at node 12, which amplifies the source interpolation error
by 10000. Strict qualification and tolerances are unchanged.

The paired reports used the same corpus e8285746c73a8229cf922487c8ccfcf6bea61bce
and prebuilt engine, with valid source provenance in both runs. Artifacts
are under E:/Artifacts/Spice-deck-blockers-65a411f. The baseline-cohort
summary SHA-256 is 48559446199d0ccac17882e601d6d8bb64f250f0f7a157c6a4997f0697c14b98.
The updated cohort summary SHA-256 is
d418948ee6b7684c6e98f22b24f85965e1c96c127da87455ded1a5702ab1fd1a.
The polarity-audit.json SHA-256 is
e754bde23645464837d3b10f013b27088f05afd5e82e768d35bfc7881d7e26f1.
The exploratory broad baseline was interrupted after the matched cohort
was available. This checkpoint does not count the incomplete census.

Frontend verification passed 626 tests with 37 opt-in skips and included
the existing seeded properties. Managed coverage measured 77.53% line
and 61.19% branch. A current-output sign mutant produced -10 S and was
caught by the controlled-source stamp test. StrictDoc parsed 52 documents
with 437 unique IDs and 405 resolved parents. Rust coverage and standalone
fuzzing were outside this checkpoint.

## Eight original-deck blockers

The follow-up used untouched ngspice corpus files pinned at
`e8285746c73a8229cf922487c8ccfcf6bea61bce`, the installed ngspice 47+
KLU build, and a frozen local Rust engine. The unit of blocker count is a
distinct failed preparation or execution boundary, and the unit of corpus
count is a path. Byte-identical paths do not increase the blocker count.

| Blocker | Original-deck evidence |
| --- | --- |
| Batch guard before a root analysis | The batch branch of `if-batchmode.cir` now dispatches its root transient. Terminal interactive/reporting work is omitted explicitly. |
| Legacy listing options | The original `tests/filters/lowpass.cir` still refuses on an unsupported directive when only the source fix is enabled. |
| Disabled distortion source coefficients | The same low-pass deck still refuses source preparation when only the options fix is enabled. Both fixes together yield agreement at OP and all 31 AC frequencies. |
| Resistor model instance default | The original `instance-defaults.cir` now executes and agrees at OP. Unmodified ngspice independently reports `INFO: ok` for its terminal assertion. |
| MOS1 model geometry | The frozen originals refused the MOS1 profile. Native instrumentation independently confirms model L/W of 3/9 µm, explicit instance override 5/10 µm and default 100/100 µm. |
| Nested DC execution | Both unchanged MOS1 originals still refuse the nested sweep with geometry and recovery enabled but nested DC removed. The primary axis is inner, and the comparator validates the independent outer voltage column. |
| Nonlinear DC recovery | With geometry and nested DC enabled, both MOS1 paths still have three line-search failures each under direct Newton with a 200-iteration limit. The same original points converge with direct-first bounded source continuation. |
| BJT substrate lowering | The original `probe-i-dev.cir` refused its four-terminal BJT. Substrate lowering alone reaches the entire requested grid, completing 194/201 points. With bounded DC recovery all 201 points execute. |

The parallel BJT path additionally needs bounded terminal `settype power`
admission. That preserves the solved values and records the metadata as a
reporting omission. The grouped and bare-DC PWL source forms are covered by
focused regressions and an independent ngspice fixture. They are not counted
as an additional original-deck execution.

The integrated 11-path replay completed in 357.95 seconds with valid source
provenance at `0a78aff758af44c633cfda31f226634c68f70598`. Eight formerly
blocked original paths executed. Four agreed with default ngspice: two
MOS1 paths with 306 points each, the original low-pass and the resistor
default fixture. Both batch RC paths still differ by up to 287.982 µV at
the output. Both BJT paths execute 201/201 points and still differ by up
to 109.917 µV at the collector under the default native tolerances. A
separate tighter native comparison reduced the collector discrepancy to
5.759 nV. No comparator thresholds were changed. The repository low-pass
regression also agreed. Both `func_cap.sp` paths remain preparation refusals.
These are cohort counts, not a refreshed full-corpus total.

Run the existing `scripts/Run-RequestedCorpus.cs` with the exact source
checkout, pinned corpus, frozen engine, ngspice, `integrated-inventory.json`,
a fresh output folder and admission budget 720. The raw output is
`E:/Artifacts/Spice-eight-source-9ce8c80/integrated-replay`.
Its `summary.json` SHA-256 is
`28e402d0c15e1f2cd0fe967776973f2d3b9151ebf97252d6140a96427ffe4355`.
The frozen engine SHA-256 is
`037406562cd2518633d4d05debceb3c5cf3252f16a52f4b68d9bbdde318b4cce`
and the ngspice binary SHA-256 is
`12bc57018aa5142d28065704d09bc6b06da2a3886f7864ca042ed4824ccf910f`.
The ablation summaries are in the sibling E: roots
`Spice-passive-lowpass-9ce8c80`, `Spice-eight-model-9ce8c80`
and `Spice-eight-ctrl-9ce8c80`.

Final managed verification passed 684 frontend tests with 26 opt-in skips
and 471 syntax tests with one exFAT symlink skip. Coverlet reported
18,558/23,676 lines and 4,937/7,880 branches in the frontend run, and
6,755/10,289 lines and 2,467/4,120 branches in the syntax run. These
are separate instrumentation scopes. Rust and ngspice coverage were
outside the managed measurements. The runs included 512 bounded
malformed-input cases at replay seed `362,20261031`, 128 resistor
default cases at seed `20261107`, and the existing seeded properties.
Focused mutations were detected for the batch guard, disabled distortion
guard, resistor alias, MOS1 fallback, BJT substrate reference, first
continuation step and independent secondary-axis comparison. No broad
mutation score is inferred.

The post-replay grouped PWL and DISTOF interaction was reproduced as
one failing regression, then verified by the frontend suite. Independent
ngspice fixture evidence for the combination has 0.9 V at both OP and
transient time zero. The 11-path totals refer to the pinned replay above.
The grammar report resolved 100 of 103 generated rules to assessments.
StrictDoc 0.30.4 parsed 55 documents, 445 unique IDs and 410 parent
relations. Logs and coverage XML are under the same E: root.

## MOS1 characterization with a floating gate supply

The `compat/ngspice/mos1-floating.cir` fixture sweeps drain voltage
inside a gate-to-source sweep with the source biased at 0.2 V. It uses
model L/W defaults and the existing bounded nonlinear DC policy. Native
RAW supplies both terminal voltages. The comparator subtracts them to
check the outer grid independently and refuses missing or non-voltage
terminal columns. The grounded-axis comparison remains covered.
The `compat/ngspice/legacy-zero.cir` fixture uses reporting options and
decimal/scientific spellings of disabled distortion coefficients.
The regression also refuses a nonzero coefficient that would underflow
to zero if converted first.

Both new regressions failed before the changes. The ngspice 47+ replay
then agreed on all mapped values across 49 MOS1 DC points and 31 legacy
AC points. It ran against base `1691f131cf4309f9f8319fd0dd1c8b0ca773d55e`
plus the working changes retained by the runner's source hash snapshot.
The valid replay summary SHA-256 is
`28c1cede213f303bf77fc0a7e36de20b56e5cd214a79a7df76ac072f6f1255f3`.
Run `scripts/Run-RequestedCorpus.cs` with the current checkout, pinned
corpus, existing engine and ngspice, `inventory.json`, a fresh output
directory and 180 seconds. The inventory, native RAW and logs are under
`E:/Artifacts/Spice-characterization-1691f13`.

Frontend verification passed 671 tests with 37 opt-in skips. Syntax passed
471 tests with one exFAT symlink skip and included the 512 malformed-input
cases at replay seed `362,20261031`. Frontend Coverlet measured 18,401
of 23,686 managed lines and 4,900 of 7,898 branches. Native coverage is
outside that scope. StrictDoc parsed 55 documents with 445 IDs and 410
resolved parent relations.
A disposable comparator mutant that omitted the negative terminal voltage
failed the floating-axis test while the grounded-axis test still passed.
The replayed changes are in `50e04b1` and `28ca83d`.

## MOS1 and BJT terminal resistances

The MOS1 fixture uses RD=35 ohms and RS=25 ohms with overlap capacitance.
It agreed with ngspice 47+ at OP, all 11 DC bias points and all 13 AC
frequencies. The level-1 NPN fixture uses RB=100 ohms, RC=35 ohms and
RE=25 ohms with junction capacitance. It agreed at OP and all 13 AC
frequencies. All 11 DC points executed. At default native tolerances
two collector current points and one base current point disagreed.
The maximum differences were 2.1262 microamperes and 21.2622 nanoamperes.
A separate native-only replay at RELTOL=1e-9, ABSTOL=1e-12 and VNTOL=1e-9
reduced the largest absolute differences to 3.815 and 0.900 picoamperes
respectively. The default native classification remains a disagreement.

The front end reuses existing resistor elements and private nodes. Native
MOS1 and BJT stamping are the independent reference. Structural tests
also cover PNP attachment, negative terms and refusal of RBM and IRB.
Three new cases failed before implementation and passed afterward. In a
disposable copy, disabling the MOS source drop and bypassing the BJT
intrinsic base caused all three cases to fail again. The broader frontend
suite passed 680 tests with 37 opt-in skips, including existing seeded
properties. Managed coverage was 18,414/23,699 lines and 4,939/7,934
branches. StrictDoc parsed 55 documents and 447 IDs. Native and Rust
coverage were outside the managed run.

The replay used base `c5166d1ab3b6c23c3f620be790f1c548f4998058` and
the working source recorded by the immutable hash gate.
`E:/Artifacts/Spice-device-resistance-c5166d1/replay/summary.json` has
SHA-256 `95909041cf65f2b7ff86c3bb4115e095e74cb304bb6b53851ec7211e5505bcd0`.
The same root contains the inventory, the two reference runs, tests,
mutation logs and coverage XML.

For GaN power switching, the first reuse candidate is EPC2218 V5 PSpice.
Vendor subcircuits preserve the declared pin order and can be compared
through the existing requested-deck runner once the exact model and
license are available. The official file could not be downloaded from
this Windows session because DNS resolution failed. No vendor model
was redistributed or executed. For compact HEMT characterization,
ASM-HEMT is a separate Verilog-A candidate. The installed ngspice
recognized its OSDI loading command in a missing-library probe, and no
installed OpenVAF executable or compiled GaN model was found. Neither
route has GaN numerical evidence yet. A first acceptance run should
freeze one model revision, verify pin mapping and compare bias currents
before testing gate charge and switching transients.

References: https://epc-co.com/epc/design-support/device-models/pspicem
and https://openvaf.github.io/docs/help/compact-models/.
