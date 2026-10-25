# Shell orchestration only. Run from repository root with pinned Rust/.NET available.
$ErrorActionPreference = 'Stop'
./scripts/Test.ps1
$exe = Join-Path (Get-Location) ('target/debug/circuit-simulator' + $(if ($IsWindows) { '.exe' } else { '' }))
dotnet run --file examples/Typed.cs -- $exe
if ($LASTEXITCODE -ne 0) { throw 'Typed C# example failed' }
dotnet fsi examples/TypedRlc.fsx $exe
if ($LASTEXITCODE -ne 0) { throw 'Typed F# example failed' }

