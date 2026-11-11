# Original nested netlist conditions

The unchanged RSpice corpus deck `regression/misc/if-elseif.cir` now executes its
requested operating point and its original terminal assertions. It selects the
nested 300-ohm resistor, obtains `out = 300`, prints `INFO: nested .if test o.k.`,
and exits zero. Its captured `test.log` is returned as owned report data; the
simulator does not write that filename. This checkpoint adds **one** newly
executing and agreeing deck to the hourly campaign.

The generated ANTLR syntax remains unchanged in purpose. Syntax-only parsing
retains every branch. A separate execution-time selector uses existing generated
conditional arguments and the existing parameter evaluator, removes inactive
metadata atomically, then invokes ordinary requested-analysis preparation.
RSpice's pinned `rspice-core-netlist/src/netlist/parser/conditionals.rs` was
inspected for its parent-active, branch-taken and else-seen state transitions and
its suppression of unreachable conditions. This implementation reuses our
existing expression grammar and public solver contract; it does not transplant
the other parser or add a second expression interpreter. The existing reuse
decision is recorded in `reuse-assessment.sdoc` and `spice-parser-foundation-assessment.md`.

The initial requested replay agreed numerically but exposed the separate missing
`v1#branch` reporting alias. That alias now maps to the same signed MNA source
current as `i(v1)`. The final replay independently measured voltage error zero
and source-current error 4.336808689942018e-19 A against default ngspice. Existing
tolerances remain 1e-6 V or 1e-9 A absolute plus 1e-4 relative; no tolerances were
weakened. The original source SHA-256 is
`8eba794f06be8f795c8344c184569c3dc57b04a643a8303253ddbe32ef02541c`.

Verification on Windows used .NET SDK 10.0.403, xUnit 2.9.3, FsCheck.Xunit 3.5.0,
Coverlet 10.1.0 and StrictDoc 0.30.4. The syntax suite passed 477 tests with one
exFAT symlink prerequisite skip. The frontend suite passed 802 tests with 40
external-prerequisite skips. One stale negative expectation that pre-analysis
literal `let` must fail was reconciled with the already implemented scalar-prefix
contract; it now challenges a result-dependent prefix, and a positive scalar
prefix is exercised through reporting. The original failing suite result is
retained. An 80-case property uses replay `883,20261111`.

A targeted manual truth-inversion mutant (`scalar != 0` to `scalar == 0`) was
killed by seven regressions; 16 other focused cases passed. The original source
was restored and its SHA-256 checked. This is one deliberate mutant, not a full
Stryker campaign or mutation-score claim. The complete syntax suite includes its
existing randomized and malformed-input tests; a separate long fuzz campaign was
not run. No native Rust source changed or native coverage was newly measured.

Scoped Coverlet class coverage was 90.16% line / 98.33% branch for
`NetlistConditionalSelection` in the full frontend suite. Its frame and generated
closure types were separately measured at 100%/100%. `ControlReportTarget` was
100% line / 90% branch in the syntax suite. These are distinct instrumentation
scopes, not full-project coverage or proof of correctness.

The first full syntax attempt exceeded a 512 MiB process-tree budget and was
terminated at 537608192 bytes; it is not a passing run. A subsequent admitted
850 MiB guarded run passed, peaking at 545320960 bytes. The final frontend suite
peaked at 551661568 bytes under the same 850 MiB cap. Each guard reserved 450 MiB
additional available RAM and enforced a single aggregate process tree, including
descendants. Raw logs, coverage, jobs and snapshots remain under
`E:/Artifacts/Spice-hourly-if-36dd277`; no bulk outputs were directed to C:.

Reproduce the managed checks using the guarded job scripts recorded in the
evidence manifest. `scripts/Run-RequestedCorpus.cs` takes checkout, corpus,
engine, ngspice, frozen inventory, fresh output directory and admission seconds.
The one-deck inventory and original bytes are preserved under that E: root.

Open boundaries remain explicit. Only root conditions based on unconditional
root parameters are admitted. Conditional parameters, instance-scoped decisions,
conditional resources and temperature-dependent branches require further
elaboration. This selection is connected to `RequestedAnalysis` and its terminal
reporter; separate PZ, transfer-function and sensitivity entry points are not
qualified here. General transient convergence such as `mosmem.cir` remains open.
There is no Linux, GPU, full SPICE or production qualification claim.
