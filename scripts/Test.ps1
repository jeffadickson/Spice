# Build/test orchestration; execute from repository root with Rust and .NET on PATH.
$ErrorActionPreference = 'Stop'
cargo build --locked
if ($LASTEXITCODE -ne 0) { throw 'Rust build failed' }
cargo test --locked
if ($LASTEXITCODE -ne 0) { throw 'Rust tests failed' }
dotnet build dotnet/Simulator.Conformance/Simulator.Conformance.csproj
if ($LASTEXITCODE -ne 0) { throw '.NET build failed' }
$exe = Join-Path (Get-Location) ('target/debug/circuit-simulator' + $(if ($IsWindows) { '.exe' } else { '' }))
dotnet run --no-build --project dotnet/Simulator.Conformance/Simulator.Conformance.csproj -- $exe
if ($LASTEXITCODE -ne 0) { throw 'Managed contract tests failed' }
dotnet test dotnet/Simulator.Client.Tests/Simulator.Client.Tests.csproj
if ($LASTEXITCODE -ne 0) { throw 'Discoverable managed contract tests failed' }

