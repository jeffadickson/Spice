# Shared controls and grammar implementation results

Source checkpoints: fdc4e6c (bounded control admission) and bb19799 (grammar map).
The clean bb19799 map enumerates 93 productions: 90 reviewed associations and 3
unassessed catchalls. Associations include infrastructure, explicit refusals and
unexecuted observations. Its second view exposes 111 historical control headings,
92 without a reviewed concrete association. Twenty registered headings have actual
generated-metadata witnesses. Historical reach counts overlap and do not predict
newly executable circuits. Current support metadata supersedes the older
ANTLR-elaboration-absent assessment; the historical table remains archived.

The product accepts bounded simple-name let/define/unlet reporting declarations
only after the final analysis/run, retaining each as postprocessing_not_executed.
Root observable/axis replacement, nonlocal deletion, earlier/interleaved
statements, substitutions and unsupported effects refuse atomically. Display,
listing and literal xbrushwidth 1..64 are reporting omissions. A local vector or
function evaluator and general control interpreter remain missing.

## Verification

Final full suites: 542 frontend passed / 22 prerequisite skips; 454 syntax passed/
1 exFAT symbolic-link skip. The new 100-case FsCheck replay is 3404,20261105;
the metadata contract caught the initial invalid even gamma, which was corrected.
Owned original suffix fixtures agree on 0.5 V output and -0.5 mA source current on
LU/faer; native alone evaluates the reporting expression to 1.5 V. The product
correctly labels that expression unexecuted. No new Rust code was changed.

Selected Stryker.NET 5.0.0 outcomes:

| Scope | Killed | Survived | Uncovered | Compile-invalid | Ignored |
|---|---:|---:|---:|---:|---:|
| ControlSuffixAdmission + RequestedControlPreparation |143|12|3|23|2|
| GrammarImplementationInventory |46|1|0|2|2|

Displayed scores are 90.51% and 97.87%. The ordering-direction map survivor remains
open, not relabelled equivalent or killed. The initial map campaign's one timeout
is retained separately; the final map campaign has none. Out-of-scope generated
candidates are not qualified by these selected-file counts. Strengthened tests
detected 27 of 42 original open control identities and 16 of 17 map identities.
The resource-failed first mutation build tested no candidates.

Mapped coverage, with reported captured-helper mappings only:

| File / scope | Lines | Branches |
|---|---:|---:|
| ControlSuffixAdmission (frontend suite) |22/23|37/44|
| RequestedControlPreparation (frontend suite) |39/40|88/90|
| ControlExtraction (frontend suite) |39/39|66/70|
| GrammarImplementationInventory (syntax suite) |30/30|40/40|
| GrammarImplementationMap helper (positive CLI run) |63/63|89/106|

These scopes are separate and do not establish whole-simulator coverage.
Three malformed CLI cases (stale production, missing requirement and escaping
source path) refuse without creating a report directory. Helper error branches
were not all coverage-instrumented. The first unset-artifact-root syntax run
failed the Windows symlink privilege prerequisite; the final configured E: run
uses the existing explicit exFAT skip. No permissions or global settings changed.

Real StrictDoc 0.30.4 parses 51 documents / 408 IDs / 381 parent links. Sphinx 9.1.0 builds
with warnings as errors and compiled client/syntax/frontend XML. The rendered
HTML map was visually inspected. Fresh read-only reviews found no important
source or qualification blocker; no external expert participation is implied.

## Frozen corpus

The clean bb19799 run completed all 928 paths in 1,015.659 seconds with valid
provenance and matching inventory against the clean 26cfad9 baseline. The
offline ranker structurally audited 894 paths and confirmed exactly two changed
primary statuses. Mirrors count as separate paths.

| Stage | Previous | Current | Change |
|---|---:|---:|---:|
| Syntax accepted | 916 | 916 | 0 |
| Circuit prepared | 85 | 85 | 0 |
| Requested analyses executed | 23 | 25 | +2 |
| Native numerical agreement | 11 | 12 | +1 |

* `examples/probe/ac-test.cir`, formerly refused at display, agrees across all
  16 AC points, three node voltages and its source branch current. The largest
  voltage discrepancy is 3.05e-16 V; three output instructions remain omitted.
* `examples/measure/meas_expression.cir`, formerly refused at let, executes its
  original 501-sample transient, with eight explicit reporting/postprocessing
  omissions. Both engine voltages match their independent ideal sine equations
  at every returned time. Native comparison still disagrees (maximum voltage
  difference 0.000397890714 V), consistent with the prior sine-grid diagnostic.
  The measured native status is preserved; no tolerance was changed.

Thirteen executed paths disagree numerically, and 903 paths lack native
agreement evidence. Four requested executions failed separately. Among reached
prepared request refusals, the leading diagnostics are if (14 paths),
postprocessing before a later analysis (12) and unsafe/unsupported tokens (9).
These are current blocker counts, not promised unlocks. Prioritize finite
conditional semantics and classify substitutions/separators individually; assess
reuse before implementing general vector evaluation or stateful control programs.

The final owned native recheck compared the engine source current directly on
both LU and faer, as well as the root voltage. Both passed. The code checkpoint
for the full corpus and the clean grammar map is bb19799; this follow-up adds
bounded evidence and strengthens the test assertion without changing runtime
code. Compatibility run duration includes the workflow and shared host load.

Raw requests, responses, logs, coverage, mutation reports and failed-attempt logs
remain under E:/Artifacts/Spice-controls-20261104T011818Z. Bounded verification is
in evidence/shared-control-verification.json and grammar-map-verification.json.
CI stayed off; full SPICE, GPU, Linux and production qualification remain open.
