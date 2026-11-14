#requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Snapshot,
    [Parameter(Mandatory)][string]$TargetDirectory,
    [Parameter(Mandatory)][string]$Provider,
    [Parameter(Mandatory)][string]$OutputDirectory
)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
foreach($path in @($root,$Snapshot,$TargetDirectory,(Split-Path $OutputDirectory -Parent))){if(!(Test-Path -LiteralPath $path -PathType Container)){throw "Missing directory: $path"}}
if(Test-Path -LiteralPath $OutputDirectory){throw 'Fresh test output required'}
if(!(Test-Path -LiteralPath $Provider -PathType Leaf)){throw 'Missing trusted provider'}
if($IsWindows -and [IO.Path]::GetPathRoot([IO.Path]::GetFullPath($OutputDirectory)) -ne 'E:\'){throw 'Keep test outputs on E:'}
New-Item -ItemType Directory -Path $OutputDirectory | Out-Null
$manifest=Get-Content -LiteralPath (Join-Path $Snapshot 'build-manifest.json') -Raw|ConvertFrom-Json
$engine=Join-Path $Snapshot 'circuit-simulator.exe'
$engineHash=(Get-FileHash -LiteralPath $engine).Hash.ToLowerInvariant()
if($engineHash -ne $manifest.engine_sha256){throw 'Snapshot executable differs from its manifest'}
$graph=@(Get-Content -LiteralPath (Join-Path $Snapshot 'cargo-artifacts.jsonl')|ForEach-Object {$_|ConvertFrom-Json}|Where-Object {$_.reason -eq 'compiler-artifact'})
$deps=Join-Path $TargetDirectory 'debug/deps';$externs=@()
# Cargo artifacts are mutable even when their paths contain a crate hash. Record
# the complete rlib dependency set before compilation and reject any changed or
# newly appearing artifact afterward. CLI and library scopes remain separate.
$dependencyHashes=[ordered]@{}
foreach($file in (Get-ChildItem -LiteralPath $deps -Filter '*.rlib' -File|Sort-Object Name)){$dependencyHashes[$file.FullName]=(Get-FileHash -LiteralPath $file.FullName).Hash}
$providerHash=(Get-FileHash -LiteralPath $Provider).Hash
foreach($name in @('circuit_simulator','serde_json','num_complex','spice_intel_host','proptest')){
    $files=@($graph|Where-Object {$_.target.name -eq $name}|ForEach-Object {$_.filenames}|Where-Object {$_ -like '*.rlib'}|Select-Object -Unique)
    if(!$files.Count){$files=@(Get-ChildItem -LiteralPath $deps -Filter "lib$name-*.rlib" -File|ForEach-Object FullName)}
    if($files.Count -ne 1){throw "Expected exact test dependency: $name"}
    $externs+=@('--extern',"$name=$($files[0])")
}
$saved=@{};foreach($name in @('SPICE_INTEL_HOST','CARGO_MANIFEST_DIR','CARGO_BIN_EXE_circuit-simulator','TEMP','TMP','RUST_TEST_THREADS','VIENNACL_CACHE_PATH','NEO_CACHE_DIR','LLVM_PROFILE_FILE')){$saved[$name]=[Environment]::GetEnvironmentVariable($name,'Process')}
$before=Get-Location
try {
    $env:SPICE_INTEL_HOST=[IO.Path]::GetFullPath($Provider);$env:CARGO_MANIFEST_DIR=$root
    [Environment]::SetEnvironmentVariable('CARGO_BIN_EXE_circuit-simulator',(Join-Path $Snapshot 'circuit-simulator.exe'),'Process')
    $env:TEMP=$OutputDirectory;$env:TMP=$OutputDirectory;$env:RUST_TEST_THREADS='1'
    $cache=Join-Path $OutputDirectory 'opencl-cache';New-Item -ItemType Directory -Path $cache|Out-Null
    $env:VIENNACL_CACHE_PATH=$cache+'/';$env:NEO_CACHE_DIR=$cache
    $results=@()
    foreach($test in @('intel_backend','providers','campaign_cli')){
        $source=Join-Path $root "tests/$test.rs";$binary=Join-Path $OutputDirectory "$test.exe"
        & rustc --edition=2024 --test $source --cfg 'feature="intel-opencl"' @externs -L "dependency=$deps" -o $binary 2> (Join-Path $OutputDirectory "$test.build.log")
        if($LASTEXITCODE){throw "Test compilation failed: $test"}
        Set-Location -LiteralPath $OutputDirectory
        & $binary --test-threads=1 *> (Join-Path $OutputDirectory "$test.log")
        $results+=@{test=$test;exit_code=$LASTEXITCODE;source_sha256=(Get-FileHash -LiteralPath $source).Hash.ToLowerInvariant()}
        if($LASTEXITCODE){throw "Snapshot test failed: $test"}
    }
    # Include the actual owned source and real public library types. This narrow
    # instrumentation measures residual-gate unit coverage, not GPU/C++ coverage.
    $driver=Join-Path $OutputDirectory 'gate.rs';$source=(Join-Path $root 'src/backend_intel.rs').Replace('\','/')
    @('mod error { pub use circuit_simulator::error::*; }','mod linalg { pub use circuit_simulator::linalg::*; }',('#[path="'+$source+'"] mod under_test;'))|Set-Content -LiteralPath $driver
    $binary=Join-Path $OutputDirectory 'gate.exe'
    & rustc --edition=2024 --test $driver -C instrument-coverage @externs -L "dependency=$deps" -o $binary 2> (Join-Path $OutputDirectory 'gate.build.log')
    if($LASTEXITCODE){throw 'Owned gate coverage build failed'}
    $env:LLVM_PROFILE_FILE=Join-Path $OutputDirectory '%m-%p.profraw'
    & $binary *> (Join-Path $OutputDirectory 'gate.log');if($LASTEXITCODE){throw 'Owned gate tests failed'}
    $sysroot=(& rustc --print sysroot).Trim();$tools=Join-Path $sysroot 'lib/rustlib/x86_64-pc-windows-msvc/bin'
    $profiles=@(Get-ChildItem -LiteralPath $OutputDirectory -Filter '*.profraw' -File|ForEach-Object FullName)
    & (Join-Path $tools 'llvm-profdata.exe') merge -sparse @profiles -o (Join-Path $OutputDirectory 'gate.profdata');if($LASTEXITCODE){throw 'Profile merge failed'}
    & (Join-Path $tools 'llvm-cov.exe') export $binary ('-instr-profile='+ (Join-Path $OutputDirectory 'gate.profdata')) > (Join-Path $OutputDirectory 'gate-coverage.json')
    if($LASTEXITCODE){throw 'Coverage export failed'}
    $after=@(Get-ChildItem -LiteralPath $deps -Filter '*.rlib' -File)
    if($after.Count -ne $dependencyHashes.Count){throw 'Cargo dependency set changed during tests'}
    foreach($file in $after){if(!$dependencyHashes.Contains($file.FullName) -or $dependencyHashes[$file.FullName] -ne (Get-FileHash -LiteralPath $file.FullName).Hash){throw "Cargo dependency changed: $($file.Name)"}}
    if($engineHash -ne (Get-FileHash -LiteralPath $engine).Hash.ToLowerInvariant() -or $providerHash -ne (Get-FileHash -LiteralPath $Provider).Hash){throw 'Test runtime changed'}
    [ordered]@{tests=$results;engine_sha256=$engineHash;provider_sha256=$providerHash.ToLowerInvariant();dependency_sha256=$dependencyHashes;runtime_provenance_valid=$true;test_scope='intel_backend/providers link the recorded Cargo libraries; campaign_cli invokes the separately hashed snapshot executable';coverage_scope='Actual src/backend_intel.rs residual-gate unit tests; runtime GPU wrapper, native host/C++/kernels and other core files uninstrumented';branch_coverage=$null;property_seed=2026111404;property_cases=16}|ConvertTo-Json -Depth 6|Set-Content -LiteralPath (Join-Path $OutputDirectory 'summary.json')
} finally {Set-Location -LiteralPath $before.Path;foreach($name in $saved.Keys){if($null -eq $saved[$name]){Remove-Item -LiteralPath ("Env:"+$name) -ErrorAction SilentlyContinue}else{[Environment]::SetEnvironmentVariable($name,$saved[$name],'Process')}}}
