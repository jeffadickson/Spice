# Orchestrate Rust/LLVM tools with native PowerShell; substantive mutation logic is C#.
$ErrorActionPreference = 'Stop'
$root = (Get-Location).Path
$coverageRoot = Join-Path $root 'build/coverage'
New-Item -ItemType Directory -Force -Path $coverageRoot | Out-Null
$oldFlags = $env:RUSTFLAGS
$oldTarget = $env:CARGO_TARGET_DIR
$oldProfile = $env:LLVM_PROFILE_FILE
try {
    $env:RUSTFLAGS = '-C instrument-coverage'
    $env:CARGO_TARGET_DIR = Join-Path $root 'target/coverage'
    # A unique run directory prevents stale profile counts from contaminating this run.
    $profileRoot = Join-Path $coverageRoot ([guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $profileRoot | Out-Null
    $env:LLVM_PROFILE_FILE = Join-Path $profileRoot 'run-%m-%p.profraw'
    cargo test --locked
    if ($LASTEXITCODE -ne 0) { throw 'Instrumented tests failed' }
    $messages = @(cargo test --locked --no-run --message-format=json)
    if ($LASTEXITCODE -ne 0) { throw 'Artifact discovery failed' }
    $executables = @($messages | ForEach-Object { $_ | ConvertFrom-Json } |
        Where-Object { $_.reason -eq 'compiler-artifact' -and $_.executable } |
        ForEach-Object { $_.executable } | Select-Object -Unique)
    $sysroot = (rustc --print sysroot).Trim()
    $hostLine = (rustc -vV | Select-String '^host:').ToString()
    $hostTarget = $hostLine.Substring(6).Trim()
    $tools = Join-Path $sysroot ('lib/rustlib/' + $hostTarget + '/bin')
    $extension = if ($IsWindows) { '.exe' } else { '' }
    $profdata = Join-Path $tools ('llvm-profdata' + $extension)
    $llvmCov = Join-Path $tools ('llvm-cov' + $extension)
    $profile = Join-Path $profileRoot 'merged.profdata'
    $raw = @(Get-ChildItem -LiteralPath $profileRoot -Filter '*.profraw' | ForEach-Object FullName)
    & $profdata merge -sparse @raw -o $profile
    if ($LASTEXITCODE -ne 0) { throw 'Profile merge failed' }
    $arguments = @('export', $executables[0], '-instr-profile=' + $profile)
    foreach ($binary in $executables | Select-Object -Skip 1) { $arguments += @('-object', $binary) }
    $arguments += @(Get-ChildItem -LiteralPath src -Filter '*.rs' | ForEach-Object FullName)
    $export = & $llvmCov @arguments
    if ($LASTEXITCODE -ne 0) { throw 'Coverage export failed' }
    $data = ($export -join "`n") | ConvertFrom-Json
    $totals = $data.data[0].totals
    $summary = [ordered]@{
        tool = 'rustc instrumentation and matching LLVM tools'; rustc = (rustc --version)
        source_commit = (git rev-parse HEAD); scope = 'owned src/*.rs; test and dependency files excluded'
        lines = $totals.lines; functions = $totals.functions; regions = $totals.regions
        branch_coverage = $null
        limitations = @('Stable source instrumentation does not provide qualified branch metrics here.',
            '.NET, vendor library internals, GPU and native plugin coverage are not measured.',
            'No sparse/GPU/native plugin implementation is present.')
    }
    $summary | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath evidence/coverage.json -Encoding utf8
    $summary | ConvertTo-Json -Depth 8
} finally {
    $env:RUSTFLAGS = $oldFlags; $env:CARGO_TARGET_DIR = $oldTarget; $env:LLVM_PROFILE_FILE = $oldProfile
}

