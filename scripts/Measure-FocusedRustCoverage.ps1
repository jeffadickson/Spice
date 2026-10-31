param([Parameter(Mandatory)][string]$Snapshot,[Parameter(Mandatory)][string]$Test,[Parameter(Mandatory)][string]$Label)
$ErrorActionPreference='Stop'
if(-not(Test-Path -LiteralPath $Snapshot -PathType Container)){throw 'Missing frozen source'}
$root=(Get-Location).Path
. (Join-Path $root 'scripts/HostTools.ps1')
Import-SpiceMsvc
$env:RUSTUP_TOOLCHAIN='nightly-x86_64-pc-windows-msvc'
$env:RUSTUP_AUTO_INSTALL='0'
$env:RUSTFLAGS='-C instrument-coverage'
$env:CARGO_BUILD_JOBS='2';$env:RUST_TEST_THREADS='2'
$env:CARGO_TARGET_DIR=Join-Path $root 'target/five-area-msvc-coverage'
$parent=Join-Path $root 'build/coverage'
if(-not(Test-Path -LiteralPath $parent -PathType Container)){throw 'Coverage parent missing'}
$raw=Join-Path $parent ($Label+'-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $raw|Out-Null
$env:LLVM_PROFILE_FILE=Join-Path $raw '%m-%p.profraw'
Set-Location -LiteralPath $Snapshot
try {
    # Cargo may otherwise reuse owner artifacts carrying another snapshot's
    # source maps. Vendor dependencies remain incremental and platform-local.
    cargo clean -p circuit-simulator --target-dir $env:CARGO_TARGET_DIR
    if($LASTEXITCODE){throw 'Owned coverage artifact refresh failed'}
    cargo test --locked --test $Test
    if($LASTEXITCODE){throw 'Instrumented test failed'}
    $messages=@(cargo test --locked --test $Test --no-run --message-format=json)
    if($LASTEXITCODE){throw 'Artifact listing failed'}
    $binary=@($messages|ForEach-Object{$_|ConvertFrom-Json}|Where-Object {$_.reason -eq 'compiler-artifact' -and $_.executable -and $_.profile.test -and $_.target.name -eq $Test}|ForEach-Object executable)
    if($binary.Count -ne 1){throw 'Expected one test executable'}
    $sysroot=(& rustc --print sysroot).Trim()
    $bin=Join-Path $sysroot 'lib/rustlib/x86_64-pc-windows-msvc/bin'
    $profiles=@(Get-ChildItem -LiteralPath $raw -Filter '*.profraw' -File|ForEach-Object FullName)
    if(-not $profiles.Count){throw 'No profiles'}
    & (Join-Path $bin llvm-profdata.exe) merge -sparse @profiles -o (Join-Path $raw merged.profdata)
    if($LASTEXITCODE){throw 'Merge failed'}
    $sources=@(Get-ChildItem -LiteralPath src -Filter '*.rs' -File|ForEach-Object FullName)
    $child=@($messages|ForEach-Object{$_|ConvertFrom-Json}|Where-Object {$_.reason -eq 'compiler-artifact' -and $_.executable -and -not $_.profile.test -and $_.target.name -eq 'circuit-simulator'}|ForEach-Object executable); if($child.Count -ne 1){throw 'Expected child engine object'}; $json=& (Join-Path $bin llvm-cov.exe) export $binary[0] -object $child[0] ('-instr-profile='+ (Join-Path $raw merged.profdata)) @sources 2> (Join-Path $raw diagnostics.log)
    if($LASTEXITCODE){throw 'Export failed'}
    $data=($json -join "`n")|ConvertFrom-Json
    $json|Set-Content -LiteralPath (Join-Path $raw raw.json)
    $hashes=[ordered]@{};foreach($source in @($sources)+@(Join-Path (Get-Location).Path "tests/$Test.rs")){
        $relative=[IO.Path]::GetRelativePath((Get-Location).Path,$source).Replace('\','/')
        $hashes[$relative]=(Get-FileHash -LiteralPath $source).Hash.ToLowerInvariant()
    }
    # Incremental Cargo can retain maps from a different, source-identical
    # snapshot. Require both owned relative identity and matching source bytes.
    $owned=@()
    foreach($source in $hashes.GetEnumerator()|Where-Object Key -like 'src/*') {
        $matches=@($data.data[0].files|Where-Object {
            $_.filename.Replace('\','/').EndsWith('/'+$source.Key,[StringComparison]::OrdinalIgnoreCase) -and
            (Test-Path -LiteralPath $_.filename -PathType Leaf) -and
            (Get-FileHash -LiteralPath $_.filename).Hash.ToLowerInvariant() -eq $source.Value
        })
        if($matches.Count -gt 1){throw 'Duplicate owned maps require explicit line-level merging'}
        if($matches.Count -eq 1){$owned+=@{file=$source.Key;mapped_file=$matches[0].filename;source_sha256=$source.Value;summary=$matches[0].summary}}
    }
    if(-not $owned.Count){throw 'No owned coverage maps'}
    $count=($owned.summary.lines|Measure-Object count -Sum).Sum
    $covered=($owned.summary.lines|Measure-Object covered -Sum).Sum
    [ordered]@{tool='rustc instrument-coverage + matching LLVM';rustc=(& rustc --version);test=$Test;snapshot=$Snapshot;
        scope='Owned src/*.rs exercised by named test binary only';lines=@{count=$count;covered=$covered;percent=100.*$covered/$count};
        file_metrics=@($owned|ForEach-Object{@{file=$_.file;mapped_file=$_.mapped_file;source_sha256=$_.source_sha256;lines=$_.summary.lines}});
        aggregation='Sum per-file owned path/SHA256 matches, including source-identical cached roots; vendor/test maps and LLVM global totals excluded';
        branches=$null;source_hashes=$hashes;raw_directory=$raw;
        diagnostics=@(Get-Content -LiteralPath (Join-Path $raw diagnostics.log));
        limitations=@('Focused test coverage, not the full regression suite','MSVC nightly compiler differs from pinned GNU regression compiler','Stable qualified branch coverage unavailable; vendor/test code excluded','LLVM diagnostics are retained; any mismatched mappings limit completeness')
    }|ConvertTo-Json -Depth 8|Set-Content -LiteralPath (Join-Path $root "evidence/five-area-$Label-coverage.json")
} finally {Set-Location -LiteralPath $root}
