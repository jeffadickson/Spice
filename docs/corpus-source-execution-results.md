# Corpus execution checkpoint — November 2

**35 files now solve selected static DC through the generated frontend and Rust,
up from 17.** All earlier solves are preserved. There are 29 upstream ngspice paths
and six repository examples, with 30 distinct root-byte hashes across 35 paths.
Thirty-four files agree with native ngspice at all exposed root voltages; one
zero-port example solves but has no exposed root reference.

**Seven of these files also execute selected linear AC probes** at 1,100 and 1000 Hz
on LU and Faer, matching native real/imaginary root voltages. AC is a subset of the
35 files, not seven additional files. These are selected analyses, not execution
of their original sweeps, input control programs or transient commands.

| Original 928-row census | Files |
|---|---:|
| Selected DC compared on both providers |34|
| Solved, no exposed root reference |1|
| Lowering refused |855|
| Resource refused |30|
| Existing hash-pinned exclusions |8|

There are 920 valid roots. Seventy actual DC provider requests completed and 172
root/node/provider voltage checks passed. The AC cohort completed 14 provider
requests,42 frequency points and 138 complex root-voltage checks. No discrepancies,
provider failures or unfinished budget work were observed in the final real
corpus runs. Copies in upstream parallel trees count as file paths, not distinct
design proof. The zero-port case remains unavailable evidence, not a match.

[Results issue and fix checklist](https://github.com/jeffadickson/Spice/issues/21).
[Updated burndown](assessments/corpus-execution-burndown.svg).
Exact files, input/source/assembly hashes and outcomes:
[evidence JSON](../evidence/corpus-sources-results.json).

## Implemented runtime path

- A bounded `sourceProfile` entry in the existing ANTLR grammar recognizes DC
  assignment, signed DC/AC values, either clause order, and explicit AC-only
  amplitude. It consumes cloned generated tokens with original physical ranges;
  there is no new lexer or handwritten SPICE parser. Expressions use existing
  scoped parameter evaluation. Unresolved, duplicate and unsupported waveform
  clauses refuse atomically. Incomplete reserved keywords cannot become
  same-named parameters. Strict legacy Lower remains unchanged.
- Selected DC retains separate DC/AC source metadata. The specified AC-only DC
  default is zero; arbitrary missing engineering data is not replaced with zero.
  Unselected AC excitation is recorded, but original source cards remain intact
  in the native OP oracle.
- Only bare `acct`, `noacct` and `noinit` option tails qualify as reporting-only
  omissions. Local ngspice frontend `spiceif.c`/`options.c` and output paths show
  these set print flags. Mixed numerical options, assignments and unknown flags
  still refuse. Source evidence is implementation inspection plus native runs,
  not broad option compatibility.
- `BridgeResult.AcSources` explicitly replaces every active independent-source
  AC amplitude, including zero for DC-only sources, while preserving separate
  DC bias. Degree phase uses exp(j*omega*t). This avoids Rust's legacy API rule
  that otherwise uses nominal DC values as AC amplitudes.
- `Run-FrontendAcCorpus.cs` reuses a completed OP census, verifies the current DC
  wire request and original-source projection, and executes explicit AC probes.
  Input controls are never dispatched. Complex discrepancy uses 1e-9 V absolute
  plus 1e-8 times reference magnitude. Missing/invalid source or result data are
  failures/unavailable outcomes, not numerical agreement.

AC currently admits only previously DC-compared files with explicit AC amplitude.
That is a qualification restriction; some valid AC circuits need not have a DC
solution. Root/internal/current observables, full sweep semantics and generated
frontend transient execution remain open.

## Validation focused on execution

76 Frontend and 406 Syntax tests passed; one exFAT link-capability test skipped.
Owned native cases check AC-only DC zero, signed/parameterized source values,
negative phase, mixed sources and reporting flags. A malformed AC vector caused
the first runner to abort without a summary; review caught this. The runner now
reuses Client manifest validation and rejects nonfinite values before recording
JSON. Short, oversized and overflowing-number synthetic cases preserve both
provider failures and complete summaries. Synthetic results never qualify Rust.

The existing five-case paired ngspice benchmark passed with one warmup and three
measured iterations. Debug Rust and concurrent load do not support a release
performance ranking; process memory was unavailable. No Rust numerical code or
new dependency was introduced. The prior SpiceSharp/ngspice reuse decisions and
approved ANTLR/Rust architecture remain in force.

StrictDoc 0.30.4 parsed 21 documents,332 IDs and 320 parent links. New
SEM-SOURCE-001, SEM-REPORT-OPTIONS-001 and SEM-ACPROBE-001 remain Draft. Sphinx
and compiled Syntax/Frontend/Client XML documentation build with warnings as
errors. Collector scope:886/898 handwritten lines 98.6%,1074/1220 branches 88%.
This denominator is incomplete: captured local functions/callbacks and both
file-app coordinators are unmeasured, generated code is excluded, and no fresh
native coverage was collected. Coverage is not correctness proof.

Following the user's priority change, no new long mutation, fuzz or soak campaign
ran. Existing bounded FsCheck properties and malformed-input regressions ran.
Windows-only execution; no GPU/Linux/NativeAOT/full-SPICE/production claim.
Read-only AI review identified the malformed-result issues; it is not external
human review. Main's unrelated sheet-resistor work was excluded and preserved.

## Reproduce and next execution gaps

Raw artifacts: `E:/Artifacts/Spice-corpus-sources-20261102T152846Z`.
Use the checked-directory setup in `docs/semantic-frontend.md` and existing pinned
ANTLR jar/SDK/tools. Build `Run-FrontendCorpus.cs` with an E: `--artifacts-path`,
`GrammarOutput` and `AntlrJar`; pass checkout, ngspice corpus, Rust executable,
ngspice executable, completed syntax inventory, fresh output,
`selected-linear-op`, and admission seconds. The final run used1800 seconds.

For selected AC, build `Run-FrontendAcCorpus.cs` with separate E: output/grammar
paths, then pass checkout, completed OP output directory, Rust executable,
ngspice executable and fresh AC output. The admission budget is 900 seconds,
checked between candidate files; child operations are bounded to 30 seconds.
The probe grid is deliberately explicit. It does not claim to run `.ac` sweep
specifications from input files.

For native integration tests also set `SPICE_FRONTEND_AC_DLL` and
`SPICE_FRONTEND_FAULT_ENGINE`, alongside the OP integration variables documented
previously. Build the test-only fault file app separately. Missing prerequisites
skip explicitly. Raw logs/builds stay local; bounded evidence is committed.

First refusals now expose 410 active-device,278 physical-directive,58 source-profile,
31 other primitive-header,55 empty-circuit,12 parameter,8 expression,2 override
and one reserved-ground gap. These first diagnostics may mask later failures;
they are not independent causes. Priority is bounded controlled/model families,
waveform/transient execution, then explicit numerical-option/global-node semantics,
using the same original-source native comparisons. Do not resume mutation-score
work in place of those runtime improvements.

Source checkpoints: `015af36` (source/reporting semantics) and `d0fc145` (AC execution/validation).
