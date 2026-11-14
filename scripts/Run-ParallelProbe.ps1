#requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidateSet('Cpu','Intel')][string]$Probe,
    [switch]$TestOnly,
    [string]$ArtifactRoot = $env:CODEX_ARTIFACT_ROOT
)

$ErrorActionPreference = 'Stop'
if (!$ArtifactRoot -and $IsWindows) { $ArtifactRoot = 'E:/Artifacts' }
if (!$ArtifactRoot -or !(Test-Path -LiteralPath $ArtifactRoot -PathType Container)) {
    throw 'Set CODEX_ARTIFACT_ROOT to an existing artifact root.'
}
$checkout = Split-Path $PSScriptRoot -Parent
$source = if ($Probe -eq 'Cpu') { 'evaluation/cpu-sparse-probe/Cargo.toml' }
          else { 'evaluation/IntelGpuProbe/IntelSpmv.cs' }
$source = Join-Path $checkout $source
if (!(Test-Path -LiteralPath $source -PathType Leaf)) { throw "Probe source missing: $source" }

# Keep reports per invocation, with one incremental cache per checkout and
# probe. The exclusive lock guards both build and execution of that probe.
$root = (Resolve-Path -LiteralPath $ArtifactRoot).Path
$identity = [Text.Encoding]::UTF8.GetBytes($checkout)
$key = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($identity)).Substring(0,16)
$cache = Join-Path $root "Spice-parallel-build-$Probe-$key"
if (!(Test-Path -LiteralPath $cache -PathType Container)) {
    New-Item -ItemType Directory -Path $cache -Force | Out-Null
}
$lock = [IO.File]::Open((Join-Path $cache 'probe.lock'), 'OpenOrCreate', 'ReadWrite', 'None')
try {
    $out = Join-Path $root "Spice-parallel-$Probe-$([guid]::NewGuid().ToString('N'))"
    New-Item -ItemType Directory -Path $out -ErrorAction Stop | Out-Null
    Write-Host "Probe artifacts: $out"

    if ($Probe -eq 'Cpu') {
        $options = @('--release','--locked','--jobs','4','--target-dir',(Join-Path $cache 'target'),'--manifest-path',$source)
        cargo test @options
        if ($LASTEXITCODE -ne 0) { throw "CPU probe tests failed (exit $LASTEXITCODE)." }
        if ($TestOnly) { return }
        cargo run @options | Tee-Object -FilePath (Join-Path $out 'cpu.csv')
        if ($LASTEXITCODE -ne 0) { throw "CPU probe failed (exit $LASTEXITCODE)." }
        return
    }

    $build = Join-Path $cache 'build'
    dotnet build $source -c Release --artifacts-path $build
    if ($LASTEXITCODE -ne 0) { throw "Intel probe build failed (exit $LASTEXITCODE)." }
    # File-based app artifact layouts include the app name on released SDK 11.
    # Retain the SDK 10 layout for the repository's supported older toolchain.
    $app = Join-Path $build 'bin/IntelSpmv.cs/release/IntelSpmv.dll'
    if (!(Test-Path -LiteralPath $app -PathType Leaf)) {
        $app = Join-Path $build 'bin/release/IntelSpmv.dll'
    }
    if (!(Test-Path -LiteralPath $app -PathType Leaf)) { throw "Intel probe binary missing: $app" }

    # The self-test checks validation without loading the OpenCL driver.
    $priorTiering = $env:DOTNET_TieredCompilation
    try {
        $env:DOTNET_TieredCompilation = '0'
        dotnet $app --self-test
        if ($LASTEXITCODE -ne 0) { throw "Intel probe self-test failed (exit $LASTEXITCODE)." }
        if ($TestOnly) { return }
        dotnet $app | Tee-Object -FilePath (Join-Path $out 'intel-opencl.json')
        if ($LASTEXITCODE -ne 0) { throw "Intel GPU probe failed (exit $LASTEXITCODE)." }
    } finally {
        $env:DOTNET_TieredCompilation = $priorTiering
    }
} finally {
    $lock.Dispose()
}
