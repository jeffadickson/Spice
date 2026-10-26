# Library-based generated tests and coverage

From an existing checkout with the pinned Rust/.NET tools on PATH:

```powershell
$checkout=(Get-Location).Path
if(-not(Test-Path -LiteralPath $checkout -PathType Container)){throw 'Checkout missing'}
if(-not(Test-Path -LiteralPath (Join-Path $checkout 'Cargo.toml') -PathType Leaf)){throw 'Use the Spice checkout'}
$env:PROPTEST_CASES='256'
$env:PROPTEST_RNG_SEED='6003378856557491509'
cargo test --locked --test property_campaign -- --nocapture
if($LASTEXITCODE -ne 0){throw 'Property campaign failed'}
dotnet test dotnet/Simulator.Client.Tests/Simulator.Client.Tests.csproj
if($LASTEXITCODE -ne 0){throw 'Managed contracts failed'}
dotnet tool restore
if($LASTEXITCODE -ne 0){throw 'Pinned tool restore failed'}
dotnet run --file scripts/ManagedCoverage.cs
if($LASTEXITCODE -ne 0){throw 'Coverage collection failed'}
```

Use the hexadecimal default in property_campaign.rs or the exact printed failure
seed as the authoritative replay identifier. PROPTEST_CASES and PROPTEST_RNG_SEED
are library controls; remove temporary overrides after a local run if desired.
Proptest persists minimized failures in tests/regressions/property_campaign.txt.
Commit real regression cases with source/tool identity; never fabricate failures.

FsCheck runs 256 generated cases in ClientProperties with Replay 362,20261027.
It shrinks a failure and prints the replay information through the xUnit runner.
Keep that seed and minimized inputs when fixing a property. The subprocess fixture
tests protocol behavior only; RealEngineConformance separately calls the actual
Rust numerical executable.

ManagedCoverage.cs runs Coverlet/VSTest and pinned ReportGenerator. Its HTML and
full reports remain in build/managed-coverage; evidence/managed-coverlet.json
contains the owned-client line/branch counts and source/report hashes. The fixture,
tests, generated metadata and native/vendor internals are excluded. These counts
are not directly comparable with historical dotnet-coverage mapping.

The earlier overnight supervisor still runs its legacy seeded batches at this
checkpoint. Merely adding these library tests does not prove that the long-run
workflow has migrated; that integration is a remaining campaign task.
