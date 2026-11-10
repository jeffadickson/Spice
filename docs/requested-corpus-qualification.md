# Individual requested-analysis corpus qualification

This coordinator reports four distinct stages over one frozen inventory: source syntax
acceptance, physical circuit preparation, actual requested-analysis execution, and
actual native numerical agreement. Each count has passed, refused/failed and unknown
values. Unknown is rendered gray and printed explicitly in `burndown.svg`. The chart
is a current measured snapshot, without invented historical burndown points.

`OperatingPointSelection.Select` is used only for physical preparation metadata and
observable mapping. Its selected OP never counts as a requested analysis. The actual
work comes exclusively from `RequestedAnalysis.Prepare`: ordinary root `.op`, `.dc`,
`.ac` and `.tran`, retaining every repeated directive's identity and source range.
Controls, UIC and unsupported physical/analysis semantics remain explicit refusals.
A successful source import or preparation is not numerical readiness.

Each native job retains the expanded original circuit and its one original analysis
card. Other analysis cards and recorded reporting/observation cards are blanked using
generated ranges; every omission and projection hash is recorded. The original
`.param` and original directive arguments remain in the native deck. The oracle does
not receive frequencies, sweep values or timing fields copied from our requests.
These are independent individual requested-analysis projections, not complete
control-program parity. `-n` prevents startup control files; original input controls
are rejected before any native job.

The public client starts a fresh engine for every request. Every mapped root voltage
and root V/L/E/H branch current is compared across complete requested OP/DC/AC grids.
DC direction and point count are retained. Native grid anomalies (including the local
ngspice47+ LIN2 defect), missing columns, units/shapes, duplicates and non-finite values
cannot pass. Transient native samples are linearly interpolated at every engine
computational time without extrapolation; this does not claim matching integration
methods, output resampling, or internal adaptive grids. Voltage/current tolerances
are 1e-6 V/1e-9 A plus 1e-4 relative. Axis matching is 1e-12 relative with a subnormal-scale
floor, rather than a fixed time/frequency epsilon. No dynamic-model generality follows
from passive source/divider fixtures.

Native result reuse assessment: existing `Simulator.Benchmarks.Tables` is an internal
named-print-table reader with nonnegative/increasing axes and rounded console output.
It cannot represent descending DC or all complex RAW columns. The new bounded ASCII
RAW reader therefore lives in the qualification-only support project. It accepts one
padded real/complex scalar plot and explicitly refuses binary/unpadded, additional
plots and unsupported shapes. No production parser or dependency migration was added.
Primary format reference: [ngspice rawfile.c](https://github.com/ngspice/ngspice/blob/master/src/frontend/rawfile.c),
inspected 2026-11-02. Binary versions/hashes and actual execution remain the evidence.

All children have 30-second deadlines. The admission budget is checked between engine
points, analyses and rows; jobs already admitted may finish after the budget boundary.
Every inventory row remains in the report, including budget-unattempted rows. A final
hash gate covers source/grammar/project files, loaded assemblies, executables,
inventory and loaded resource files. Changed provenance invalidates all four stage
claims and renders them unknown. Numerical agreement is not displayed as final until
the gate passes. Raw point responses are retained for auditing, but an operation with
any failed point is withheld from numerical qualification; later operations remain
independent.

## Reproduction and integration

The original 928-input inventory is
`E:/Artifacts/Spice-table-20261102T015724Z/corpus-delivery.json`, with ngspice sources
under `C:/dev/ngspice-ngspice`. Do not start that census until the integrated source
and engine are frozen. Development verification uses owned representative fixtures.
Run the following pattern with existing profile-derived checkout/executable paths and
a fresh project/run output subtree under CODEX_ARTIFACT_ROOT or E:/Artifacts:

```powershell
if (!(Test-Path -LiteralPath $checkout -PathType Container)) { throw 'Missing checkout' }
if (!(Test-Path -LiteralPath $corpus -PathType Container)) { throw 'Missing corpus' }
if (!(Test-Path -LiteralPath $artifactParent -PathType Container)) { throw 'Missing artifact parent' }
foreach ($file in @($engine, $ngspice, $inventory, $antlrJar)) {
    if (!(Test-Path -LiteralPath $file -PathType Leaf)) { throw "Missing $file" }
}
foreach ($directory in @($build, $grammar)) {
    if (!(Test-Path -LiteralPath (Split-Path $directory -Parent) -PathType Container)) { throw 'Missing build parent' }
    if (!(Test-Path -LiteralPath $directory -PathType Container)) { New-Item -ItemType Directory -Path $directory | Out-Null }
}
if (Test-Path -LiteralPath $output) { throw 'Use a fresh campaign output' }
Push-Location -LiteralPath $checkout
try {
    dotnet run --file scripts/Run-RequestedCorpus.cs --artifacts-path $build `
      "-p:GrammarOutput=$grammar" "-p:AntlrJar=$antlrJar" -- `
      $checkout $corpus $engine $ngspice $inventory $output 1800
    if ($LASTEXITCODE -ne 0) { throw "Coordinator exited $LASTEXITCODE; inspect retained summary" }
} finally { Pop-Location }
```

`$build` and `$grammar` should be unique paths under an existing writable artifact
parent; they must not be shared with concurrent builds or Stryker snapshots. Exit 0
means a completed report, including explicit unsupported/unqualified inputs, not
universal agreement. Exit 2 means budget exhaustion; exit 3 means changed provenance.
The top-level `scope` text distinguishes individual requested analyses from full input
programs. Failed/unavailable evidence remains explicit in each row/operation.

Tests are in `dotnet/Simulator.Frontend.Tests/RequestedQualificationTests.cs`; set
SPICE_CORPUS_ENGINE, SPICE_CORPUS_NGSPICE and SPICE_REQUESTED_ARTIFACTS to existing
executables and an existing writable artifact root to admit native integration tests.
Use a separate `dotnet test --artifacts-path` and `GrammarOutput` with Coverlet
`--collect:"XPlat Code Coverage"`. Seeded FsCheck RAW-tail malformed-input checks use
100 cases and replay 2371,20261103. No extra mutation campaign is part of this coordinator
slice; malformed, provenance, identity, units, shape and original-grid checks target
the qualification boundaries directly.

The thin fileapp was also executed on the seven owned rows with a one-second admission
budget: exit 2, provenance valid, all seven rows retained as unknown in every stage.
No jobs were admitted after the initial reporting overhead consumed that short budget.


Running checkpoints now sample the same monotonic admission clock as final duration.
An event-driven native-backed test observed positive running values and a nondecreasing
final value; exact samples and scoped coverage are in evidence/requested-progress-clock.json.
The already frozen62ae97d campaign source was not changed retroactively.


The level-1 JFET checkpoint used the RSpice corpus at 29c28a02 and the original jfet/jfet_vds-vgs.cir, SHA-256 1b091823aefe52c8b3b2ee7f57974847c57a909e255d08fac3d62f562f2da739. RSpice passed its suite reference. The generated frontend executed the original operating point and all 104 nested DC points through the native solver. The operating point agreed with ngspice 47+, with drain-current error below 27 pA. The original sweep remains classified as numerical disagreement because four drain-current samples exceeded the campaign tolerance, with a maximum difference of 3.10 µA.

A separate diagnostic copy set ngspice RELTOL=1e-9, ABSTOL=1e-15 and VNTOL=1e-12. On that copy all 104 drain-current values met the same comparison tolerance, with a maximum difference of 28 pA. This isolates sensitivity to the reference solve tolerances without changing the original corpus classification. Bounded source continuation recovered the original operating point, which hit the Newton iteration limit with the default startup.

Six focused tests passed. JfetPreparation had 90.9% line and 81.81% branch coverage. The original deck qualifies only the NJF DC profile. PJF polarity was tested at the lowering boundary. Separate mutation and fuzz campaigns and native line/branch coverage were outside this run. Raw evidence is under E:/Artifacts/Spice-rspice-jfet-4661368.

The JFET gate-depletion checkpoint adds CGS, CGD, PB and FC to the bounded level-1 binder. The original nominal DC junction topology is reused for displacement. The ngspice fixture compat/ngspice/jfet-gate-charge.cir qualifies a small NJF gate ramp at a fixed bias. At a 5 ps maximum step, all 8,001 native samples agreed across four voltages and two source currents against 8,011 ngspice samples. Maximum gate-voltage and gate-drive-current differences were 0.783 µV and 0.783 nA. Removing both gate capacitances from the same native request changed the gate-drive current by 3.47 µA. At 10 ps the gate-drive current exceeded the campaign tolerance, and a separate 1 MHz AC attempt hit the Newton iteration limit during bias initialization. AC and PJF transient remain unqualified.

Five additional originals from the RSpice corpus at 29c28a02 passed its own runner. In the generated requested-analysis replay, resistance/res_simple.cir agreed in transient, resistance/res_partition.cir and filters/lowpass.cir agreed in operating point and AC, mos6/simpleinv.cir refused its level-6 model, and polezero/filt_rc.cir refused the unsupported .pz program. The last two did not execute through our solver.

Ten focused tests passed, with 91.83% line and 80.35% branch coverage in JfetPreparation. The charge-disabled replay is a targeted behavioral control. Native coverage, framework mutation and a fuzz campaign were outside this run. The existing engine SHA-256 was 4F915B77C4CD28ADF76D6EAC8B7845168C0A27723FC4A8385E1865041A82F582 and ngspice SHA-256 was 12BC57018AA5142D28065704D09BC6B06DA2A3886F7864CA042ED4824CCF910F. The frozen inventory, both campaign summaries, individual tool logs, responses and coverage are in E:/Artifacts/Spice-jfet-charge-cohort-21d6b4f.
