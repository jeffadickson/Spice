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
