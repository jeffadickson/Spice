# Execute one requested SPICE deck

`scripts/Run-RequestedDeck.cs` is the supported thin command for one deck. It reuses `ResourceResolver`, generated syntax, `RequestedAnalysis.Prepare`/`PrepareExecutionAsync`/`ExecuteAsync`, and `SimulationClient`. It runs the original admitted `.op`, `.dc`, `.ac` and `.tran` requests. It requires an existing Rust engine and .NET 10; ngspice is not required. This is execution, not numerical qualification or full SPICE/control-program compatibility.

The three positional arguments are input, engine and a **fresh output directory**. Optional following arguments add explicit allowed resource directories. The input's existing parent is always allowed. Existing output, missing inputs/roots, unknown controls and unsupported resource/model/analysis profiles refuse. Admitted bounded control syntax remains governed by the public preparation API; no shell or original control interpreter is invoked.

From the checkout root, after the normal native and generated-parser setup:

```powershell
$repo = (Get-Location).Path
if (!(Test-Path -LiteralPath $repo -PathType Container)) { throw 'Missing checkout' }
$runner = Join-Path $repo 'scripts/Run-RequestedDeck.cs'
if (!(Test-Path -LiteralPath $runner -PathType Leaf)) { throw 'Run from the checkout root' }
$target = if ($env:CARGO_TARGET_DIR) { $env:CARGO_TARGET_DIR } else { Join-Path $repo 'target' }
if (!(Test-Path -LiteralPath $target -PathType Container)) { throw 'Select your existing native target directory' }
$binary = if ($IsWindows) { 'circuit-simulator.exe' } else { 'circuit-simulator' }
$engine = Join-Path $target "debug/$binary"
if (!(Test-Path -LiteralPath $engine -PathType Leaf)) { throw 'Select your existing built Rust engine' }
$artifactRoot = if ($env:CODEX_ARTIFACT_ROOT) { $env:CODEX_ARTIFACT_ROOT } else { 'E:/Artifacts' }
if (!(Test-Path -LiteralPath $artifactRoot -PathType Container)) { throw 'Choose an existing artifact root' }
$run = Join-Path $artifactRoot ('Spice-requested-deck-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $run | Out-Null
# Create only the parent. The runner must own each fresh output directory.
foreach ($name in @('bjt-ebers-moll', 'mos1-body-overlap')) {
    $input = Join-Path $repo "examples/$name.cir"
    if (!(Test-Path -LiteralPath $input -PathType Leaf)) { throw "Missing $input" }
    dotnet run --file $runner --artifacts-path (Join-Path $run 'managed-build') -- $input $engine (Join-Path $run $name)
    if ($LASTEXITCODE) { throw "Deck execution failed: $name" }
}
```

Both unchanged semiconductor examples have been executed through this path; each requests OP and AC. Their bounded nonzero-charge models remain intact, and no unsupported IC or original parameter is suppressed. Generated recognition needs the existing pinned ANTLR generator during a source build; use the repository setup or pass an existing jar with `-p:AntlrJar=<path>`. A worktree can redirect generated grammar using `-p:GrammarOutput=<private-artifact-directory>`. These are build settings, not runner arguments. Once built, `dotnet <Run-RequestedDeck.dll> <input> <engine> <fresh-output> [additional-root ...]` uses the same command without rebuilding.

The command exits 0 only for `status: executed`; execution failure, refusal or cancellation returns nonzero. Preflight usage/ownership failures return 2 without taking ownership of existing output. Every owned run publishes `summary.json` through a non-overwriting temporary-file rename. Failure to publish is itself nonzero. A crash or disk failure can leave incomplete artifacts without a summary; no recovery or power-loss guarantee is claimed.

Artifacts retain the resolved source hashes, segment mapping and expanded deck, full original plan, original operation IDs and point ordinals, prepared requests, materialized requests, returned raw SDK response payloads, initialization requests/responses, and reported-window manifests. The reported view can omit pre-TSTART samples; the raw response keeps them. Descriptor IDs are native wire IDs; use `plan.json` circuit preparation `RootNodes`/`Origins` to map original nodes/devices. Print/plot observations remain explicit metadata rather than an implicit text-table promise.

Each original point is independently initialized, as defined by the existing API. IC preparation runs once; its returned bias response and materialized request are archived before execution. A single-point `ExecuteAsync` view then clears its initialization request to avoid rerunning bias. This archives successful partial points even when another point fails, while the original operation/job retains a failed status. DC points are independent OP solves; no continuation or hysteresis parity is implied.

Limits are 120 seconds per job, 30 seconds per point, 32 operations and 1000 total client points, plus existing resource/parser limits. Cancellation is cooperative across bounded synchronous resolution/preparation and active during asynchronous client calls; OS-blocked filesystem calls have no hard wall-time guarantee. Known unattempted points are explicit. Original source and engine hashes are rechecked before an executed label can be retained. These checks do not make filesystem resolution a hostile-concurrency sandbox.

Verification: `scripts/Test-RequestedDeck.cs` drives the compiled adapter and real frozen engine. It covers RLC OP/AC/transient, DC identity/values, IC and reporting-window archival, mixed success/failure DC payload retention, unchanged BJT/MOS examples, local/explicit resources, control/resource/output/topology/argument refusals. `evidence/requested-deck-runner.json` records actual counts, adapter-only coverage and limitations. Timeout/user-cancellation and forced-disk failure were not newly fault-injected; no new mutation or broad fuzz campaign is claimed.

Measured adapter scope: 90/92 mapped lines and 106/142 mapped class-line conditional branches. Coverlet's module headline reports line rate 0.9782 / branch rate 0.7465; the raw aggregate and condition counts are retained separately. Captured Save/SaveText/CreateFolder maps are present. Reused API/native coverage is outside this adapter claim. Generic IO/transport archival exception paths and cancellation branches remain incompletely exercised.
