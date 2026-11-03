# Pinned stateless stop-grid experiment

`scripts/Run-StopGridPoc.cs` reproduces one io_ordering qualification experiment.
It does not alter the product comparator or any of the18 primary cohort statuses.
The helper reuses generated syntax, the public typed result contract and existing
ASCII RAW reader. No parser, runtime migration or dependency is introduced.

The original physical deck and `.tran 1e-5 1e-3` are byte-pinned, as are the
retained engine manifest and ngspice47+ executable. Paths are configurable; bytes
must match the pins in `StopGridQualification.cs`. In particular the original
projection has CRLF line endings, and a canonical-LF source copy will refuse.
No untrusted original controls/resources can enter this experiment. Output must
be fresh under an existing parent. Bounds:4096 original bytes,1MiB retained JSON,
101 engine samples,4096 native points,8MiB RAW,1MiB logs and30seconds per child.

Owned instrumentation registers99 `stop when time = <round-trip sample time>`
conditions before `run`, then99 finite `resume` commands and relative
`write native.raw all`/quit. Native time conditions use CKTsetBreak
(`ngspice/src/frontend/breakp.c`). No interpolation option is enabled. The result
requires99 actual pauses, completion at1ms, actual monotonic native axes and
101 unique matching rows within32ULP. Zero time must match exactly. Values are
never interpolated/extrapolated. All four voltage/current columns retain the
existing1e-6V/1e-9A absolute plus1e-4 relative tolerances.

The fresh replay retains504 native rows versus101 engine rows:403 native rows
are additional. Maximum observed time difference is2ULP, with77 bit-exact matches;
voltage errors are approximately9e-16V/9e-15V and current errors9e-19A/9e-18A.
Complete axes and hashes are local artifacts; bounded evidence is in
[stop-grid-poc.json](../evidence/stop-grid-poc.json).

This supports a separate future instrumented requested-time native oracle
profile. It does not establish equality of full internal grids or unchanged
solver history: adding breakpoints changed native step selection from108 to504
rows. Stateless gain/sine results do not qualify RLC/nonlinear/charge histories.
Timings include native execution and postprocessing, not a provider benchmark.
The helper is managed .NET10; Native AOT was not qualified.

Example on the recorded Windows environment (relocate identical pinned files as
needed):

```powershell
$repo = Join-Path $env:USERPROFILE 'Documents/Codex/2026-10-24/how/outputs/rust-circuit-simulator/work/stop-grid-qualification'
$artifactRoot = if ($env:CODEX_ARTIFACT_ROOT) { $env:CODEX_ARTIFACT_ROOT } else { 'E:\Artifacts' }
foreach ($p in @($repo, $artifactRoot)) {
    if (!(Test-Path -LiteralPath $p -PathType Container)) { throw "Missing directory: $p" }
}
$run = Join-Path $artifactRoot ('spice-stop-grid-' + [Guid]::NewGuid().ToString('N'))
if (Test-Path -LiteralPath $run) { throw 'Fresh run required' }
New-Item -ItemType Directory -Path $run | Out-Null
$case = Join-Path $artifactRoot 'Spice-compatibility-20261103T043926Z/requested-cohort-408d718/cases/0001/requested_0'
if (!(Test-Path -LiteralPath $case -PathType Container)) { throw 'Missing retained case' }
$projection = Join-Path $case 'original-analysis.cir'
$retained = Join-Path $case '00000.response.json'
$native = 'C:/dev/ngspice-install/bin/ngspice.exe'
$jar = Join-Path $env:USERPROFILE 'Documents/Codex/2026-10-24/how/outputs/rust-circuit-simulator/.tools/antlr/antlr-4.13.1-complete.jar'
foreach ($p in @($projection, $retained, $native, $jar)) {
    if (!(Test-Path -LiteralPath $p -PathType Leaf)) { throw "Missing file: $p" }
}
dotnet run --file (Join-Path $repo 'scripts/Run-StopGridPoc.cs') --artifacts-path (Join-Path $run 'build') "-p:AntlrJar=$jar" -- $projection $retained $native (Join-Path $run 'poc')
if ($LASTEXITCODE) { throw 'Supplemental experiment failed; retain its artifacts' }
```
