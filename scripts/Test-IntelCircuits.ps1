#requires -Version 7.0
[CmdletBinding()]
param(
    [string]$Provider=$env:SPICE_INTEL_HOST,
    [string]$ArtifactRoot=$env:CODEX_ARTIFACT_ROOT,
    [string]$TargetDirectory=''
)
$ErrorActionPreference='Stop'
if(!$ArtifactRoot -and $IsWindows){$ArtifactRoot='E:/Artifacts'}
if(!$ArtifactRoot -or !(Test-Path -LiteralPath $ArtifactRoot -PathType Container)){throw 'Existing artifact root required'}
if(!$Provider -or ![IO.Path]::IsPathFullyQualified($Provider) -or !(Test-Path -LiteralPath $Provider -PathType Leaf)){
    throw 'Set SPICE_INTEL_HOST to the trusted provider built by Build-IntelProvider.ps1. Unavailable hardware/provider is not a GPU pass.'
}
$root=Split-Path $PSScriptRoot -Parent
if(!(Test-Path -LiteralPath $root -PathType Container)){throw 'Missing checkout'}
$key=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($root))).Substring(0,16)
if(!$TargetDirectory){$TargetDirectory=Join-Path $ArtifactRoot "Spice-intel-target-$key"}
if($IsWindows -and [IO.Path]::GetPathRoot([IO.Path]::GetFullPath($TargetDirectory)) -ne 'E:\'){throw 'Keep Intel target artifacts on E:'}
if(!(Test-Path -LiteralPath (Split-Path $TargetDirectory -Parent) -PathType Container)){throw 'Missing target parent'}
if(!(Test-Path -LiteralPath $TargetDirectory -PathType Container)){New-Item -ItemType Directory -Path $TargetDirectory | Out-Null}
$lock=[IO.File]::Open((Join-Path $TargetDirectory 'intel-verification.lock'),'OpenOrCreate','ReadWrite','None')
$saved=@{};foreach($name in @('SPICE_INTEL_HOST','CARGO_TARGET_DIR','CARGO_BUILD_JOBS','RUST_TEST_THREADS','TEMP','TMP','VIENNACL_CACHE_PATH','NEO_CACHE_DIR')){$saved[$name]=[Environment]::GetEnvironmentVariable($name,'Process')}
$prior=Get-Location
try {
    $output=Join-Path $ArtifactRoot ('Spice-intel-circuit-tests-'+[guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $output | Out-Null
    $cache=Join-Path $output 'opencl-cache';New-Item -ItemType Directory -Path $cache | Out-Null
    $env:SPICE_INTEL_HOST=[IO.Path]::GetFullPath($Provider);$env:CARGO_TARGET_DIR=$TargetDirectory;$env:CARGO_BUILD_JOBS='1';$env:RUST_TEST_THREADS='1'
    $env:TEMP=$output;$env:TMP=$output;$env:VIENNACL_CACHE_PATH=$cache+[IO.Path]::DirectorySeparatorChar;$env:NEO_CACHE_DIR=$cache
    Set-Location -LiteralPath $root
    cargo test --locked --features intel-opencl --test intel_backend --test providers --test campaign_cli -- --test-threads=1 *> (Join-Path $output 'tests.log')
    $exit=$LASTEXITCODE
    [ordered]@{exit_code=$exit;provider_sha256=(Get-FileHash -LiteralPath $Provider).Hash.ToLowerInvariant();rustc=(& rustc --version);tests_log=Join-Path $output 'tests.log';scope='Actual Intel real/complex solves, known-solution properties, full divider circuit and bounded CLI lifecycle; not arbitrary deck qualification'}|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $output 'summary.json')
    Write-Host "Intel circuit test artifacts: $output"
    if($exit){throw "Intel circuit verification failed (exit $exit)."}
} finally {
    Set-Location -LiteralPath $prior.Path
    foreach($name in $saved.Keys){if($null -eq $saved[$name]){Remove-Item -LiteralPath ("Env:"+$name) -ErrorAction SilentlyContinue}else{[Environment]::SetEnvironmentVariable($name,$saved[$name],'Process')}}
    $lock.Dispose()
}
