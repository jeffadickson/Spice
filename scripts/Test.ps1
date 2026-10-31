# Build/test orchestration; execute from repository root with Rust and .NET on PATH.
[CmdletBinding()]
param([switch]$SyntaxOnly,[switch]$SyntaxCorpus,[string]$CorpusRoot)
$ErrorActionPreference = 'Stop'
$repository=Split-Path $PSScriptRoot -Parent
if(!(Test-Path -LiteralPath $repository -PathType Container)){throw 'Missing repository'}
if(!(Test-Path -LiteralPath (Join-Path $repository 'Cargo.toml') -PathType Leaf)){throw 'Missing repository manifest'}
if([IO.Path]::GetFullPath((Get-Location).Path) -ne [IO.Path]::GetFullPath($repository)){throw 'Run from repository root'}
$syntaxTests=Join-Path $repository 'dotnet/Simulator.Syntax.Tests'
if(!(Test-Path -LiteralPath $syntaxTests -PathType Container)){throw 'Missing syntax test project'}
& (Join-Path $PSScriptRoot 'Initialize-SyntaxParser.ps1')
dotnet test $syntaxTests -p:RestoreLockedMode=true
if($LASTEXITCODE -ne 0){throw 'Parser-only tests failed'}
if($SyntaxCorpus){
    $parameters=@{WorkerSeconds=90}
    if($CorpusRoot){$parameters.CorpusRoot=$CorpusRoot}
    & (Join-Path $PSScriptRoot 'Run-SyntaxCorpus.ps1') @parameters -RequireValidSamples
}
# This path intentionally returns before any numerical engine build or run.
if($SyntaxOnly){return}
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

