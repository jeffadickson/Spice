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
