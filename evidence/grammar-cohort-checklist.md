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
