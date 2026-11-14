#requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$TargetDirectory,
    [Parameter(Mandatory)][string]$OutputDirectory
)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
foreach($p in @($root,$TargetDirectory,(Split-Path $OutputDirectory -Parent))){if(!(Test-Path -LiteralPath $p -PathType Container)){throw "Missing directory: $p"}}
if(Test-Path -LiteralPath $OutputDirectory){throw 'Fresh output required; never replace a running engine'}
if($IsWindows -and [IO.Path]::GetPathRoot([IO.Path]::GetFullPath($OutputDirectory)) -ne 'E:\'){throw 'Keep engine outputs on E:'}
New-Item -ItemType Directory -Path $OutputDirectory | Out-Null
$saved=@{};foreach($name in @('CARGO_TARGET_DIR','CARGO_BUILD_JOBS','TEMP','TMP')){$saved[$name]=[Environment]::GetEnvironmentVariable($name,'Process')}
$before=Get-Location
try {
    Set-Location -LiteralPath $root
    $env:CARGO_TARGET_DIR=$TargetDirectory;$env:CARGO_BUILD_JOBS='1';$env:TEMP=$OutputDirectory;$env:TMP=$OutputDirectory
    # Retain Cargo's dependency graph and exact artifact metadata. Link the
    # normal CLI source at a fresh path rather than replacing target/debug's
    # executable while another process still holds that image on Windows.
    $messages=@(cargo rustc --locked --features intel-opencl --lib --message-format=json 2> (Join-Path $OutputDirectory 'library-build.log'))
    if($LASTEXITCODE){throw 'Intel library build failed'}
    $messages|Set-Content -LiteralPath (Join-Path $OutputDirectory 'cargo-artifacts.jsonl')
    $artifacts=@($messages|ForEach-Object {$_|ConvertFrom-Json}|Where-Object {$_.reason -eq 'compiler-artifact'})
    $externs=@();$selected=@{}
    foreach($name in @('circuit_simulator','serde','serde_json','tracing','tracing_subscriber','spice_intel_host')) {
        $files=@($artifacts|Where-Object {$_.target.name -eq $name}|ForEach-Object {$_.filenames}|Where-Object {$_ -like '*.rlib'}|Select-Object -Unique)
        if($files.Count -ne 1){throw "Expected exact Cargo library artifact for $name"}
        $selected[$name]=$files[0];$externs+=@('--extern',"$name=$($files[0])")
    }
    $source=Join-Path $root 'src/main.rs';$exe=Join-Path $OutputDirectory 'circuit-simulator.exe'
    & rustc --edition=2024 --crate-name circuit_simulator_intel --cfg 'feature="intel-opencl"' -C debuginfo=2 $source @externs -L "dependency=$(Join-Path $TargetDirectory 'debug/deps')" -o $exe 2> (Join-Path $OutputDirectory 'entry-link.log')
    if($LASTEXITCODE){throw 'Fresh Intel entry-point link failed'}
    [ordered]@{method='Cargo-owned library graph + rustc normal CLI source at fresh path';profile='dev unoptimized library and entry; native GPU module separately Release';rustc=(& rustc --version);main_sha256=(Get-FileHash -LiteralPath $source).Hash.ToLowerInvariant();engine_sha256=(Get-FileHash -LiteralPath $exe).Hash.ToLowerInvariant();libraries=$selected}|ConvertTo-Json -Depth 5|Set-Content -LiteralPath (Join-Path $OutputDirectory 'build-manifest.json')
    Write-Host "Intel engine: $exe"
} finally {Set-Location -LiteralPath $before.Path;foreach($name in $saved.Keys){if($null -eq $saved[$name]){Remove-Item -LiteralPath ("Env:"+$name) -ErrorAction SilentlyContinue}else{[Environment]::SetEnvironmentVariable($name,$saved[$name],'Process')}}}
