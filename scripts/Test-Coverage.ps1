# Orchestrate Rust/LLVM tools with native PowerShell; substantive mutation logic is C#.
param([string]$CoverageToolchain = '', [switch]$CheckPrerequisitesOnly)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'HostTools.ps1')
Import-SpiceTools -Root (Split-Path $PSScriptRoot -Parent)
if ($IsWindows) { Import-SpiceMsvc }
$oldToolchain = $env:RUSTUP_TOOLCHAIN
$oldFlags = $env:RUSTFLAGS
$oldTarget = $env:CARGO_TARGET_DIR
$oldProfile = $env:LLVM_PROFILE_FILE
try {
    # Explicit overrides are supported; otherwise let rustup select rust-toolchain.toml.
    if ([string]::IsNullOrWhiteSpace($CoverageToolchain)) {
        $CoverageToolchain = $env:SPICE_COVERAGE_TOOLCHAIN
    }
    if ([string]::IsNullOrWhiteSpace($CoverageToolchain)) {
        Remove-Item Env:RUSTUP_TOOLCHAIN -ErrorAction SilentlyContinue
    } else {
        $env:RUSTUP_TOOLCHAIN = $CoverageToolchain
    }
    $active = @(rustup show active-toolchain)
    if ($LASTEXITCODE -ne 0) { throw 'Cannot resolve coverage toolchain. Install the pinned rust-toolchain.toml toolchain or the explicit coverage override.' }
    $selected = ($active[0] -split '\s+')[0]
    $repair = "rustup component add llvm-tools-preview --toolchain $selected"
    $sysrootOutput = @(rustc --print sysroot)
    if ($LASTEXITCODE -ne 0) { throw "Cannot query coverage sysroot for $selected." }
    $sysroot = ($sysrootOutput -join '').Trim()
    $version = @(rustc -vV)
    if ($LASTEXITCODE -ne 0) { throw "Cannot query coverage compiler for $selected." }
    $hostLine = @($version | Where-Object { $_ -like 'host: *' })
    if ($hostLine.Count -ne 1) { throw "Missing rustc host target for $selected." }
    $hostTarget = $hostLine[0].Substring(6).Trim()
    $tools = Join-Path $sysroot ('lib/rustlib/' + $hostTarget + '/bin')
    $extension = if ($IsWindows) { '.exe' } else { '' }
    $profdata = Join-Path $tools ('llvm-profdata' + $extension)
    $llvmCov = Join-Path $tools ('llvm-cov' + $extension)
    Write-Host "Coverage toolchain: $selected; host: $hostTarget; sysroot: $sysroot"
    $toolVersions = @{}
    foreach ($executable in @($profdata, $llvmCov)) {
        if (-not (Test-Path -LiteralPath $executable -PathType Leaf)) {
            throw "Coverage prerequisite missing: $executable. Install matching tools with: $repair"
        }
        try {
            # Bypass PowerShell's document-opening fallback for non-executable files.
            $start = [Diagnostics.ProcessStartInfo]::new()
            $start.FileName = $executable
            $start.ArgumentList.Add('--version')
            $start.UseShellExecute = $false
            $start.RedirectStandardOutput = $true
            $start.RedirectStandardError = $true
            $process = [Diagnostics.Process]::new()
            $process.StartInfo = $start
            try {
                if (-not $process.Start()) { throw 'process did not start' }
                $stdout = $process.StandardOutput.ReadToEndAsync()
                $stderr = $process.StandardError.ReadToEndAsync()
                if (-not $process.WaitForExit(10000)) {
                    $process.Kill($true)
                    $process.WaitForExit()
                    throw 'version probe exceeded 10 seconds'
                }
                $toolVersion = @($stdout.GetAwaiter().GetResult(), $stderr.GetAwaiter().GetResult())
                if ($process.ExitCode -ne 0) { throw "exit code $($process.ExitCode): $($toolVersion -join ' ')" }
                if (($toolVersion -join ' ') -notmatch 'LLVM version') { throw 'version probe did not identify LLVM' }
            } finally {
                $process.Dispose()
            }
        } catch {
            throw "Coverage prerequisite cannot execute: $executable ($($_.Exception.Message)). Repair with: $repair"
        }
        $toolVersions[[IO.Path]::GetFileName($executable)] = $toolVersion -join "`n"
        Write-Host ($toolVersion -join "`n")
    }
    $runtimeDirectory = Join-Path $sysroot ('lib/rustlib/' + $hostTarget + '/lib')
    $runtime = Get-ChildItem -LiteralPath $runtimeDirectory -Filter 'libprofiler_builtins*.rlib' -ErrorAction SilentlyContinue | Select-Object -First 1
    if (-not $runtime) { throw "Coverage toolchain $selected does not include profiler_builtins. Install a coverage-capable Rust distribution for $hostTarget." }
    if ($IsWindows -and $hostTarget -like '*-msvc' -and -not (Get-Command link.exe -ErrorAction SilentlyContinue)) {
        throw 'Coverage linker link.exe is missing. Install the Visual C++ Build Tools and Windows SDK.'
    }
    if ($CheckPrerequisitesOnly) { return }
    $root = (Get-Location).Path
    $coverageRoot = Join-Path $root 'build/coverage'
    New-Item -ItemType Directory -Path $coverageRoot -Force | Out-Null
    $env:RUSTFLAGS = '-C instrument-coverage'
    $env:CARGO_TARGET_DIR = Join-Path $root 'target/coverage'
    # A unique run directory prevents stale profile counts from contaminating this run.
    $profileRoot = Join-Path $coverageRoot ([guid]::NewGuid().ToString('N'))
    if (Test-Path -LiteralPath $profileRoot -PathType Container) { throw 'Coverage profile directory unexpectedly exists.' }
    New-Item -ItemType Directory -Path $profileRoot | Out-Null
    $env:LLVM_PROFILE_FILE = Join-Path $profileRoot 'run-%m-%p.profraw'
    cargo test --locked
    if ($LASTEXITCODE -ne 0) { throw 'Instrumented tests failed' }
    $messages = @(cargo test --locked --no-run --message-format=json)
    if ($LASTEXITCODE -ne 0) { throw 'Artifact discovery failed' }
    $executables = @($messages | ForEach-Object { $_ | ConvertFrom-Json } |
        Where-Object { $_.reason -eq 'compiler-artifact' -and $_.executable } |
        ForEach-Object { $_.executable } | Select-Object -Unique)
    if ($executables.Count -eq 0) { throw 'Artifact discovery returned no executables; coverage was not measured.' }
    $profile = Join-Path $profileRoot 'merged.profdata'
    $raw = @(Get-ChildItem -LiteralPath $profileRoot -Filter '*.profraw' | ForEach-Object FullName)
    if ($raw.Count -eq 0) { throw 'Instrumented tests produced no raw profiles; coverage was not measured.' }
    $mergeDiagnostics = Join-Path $profileRoot 'llvm-profdata.stderr.txt'
    # Profile counts grow with child processes; avoid Windows' command-line limit.
    $rawList = Join-Path $profileRoot 'profraw-files.txt'
    $raw | Set-Content -LiteralPath $rawList -Encoding utf8
    & $profdata merge -sparse ('--input-files=' + $rawList) -o $profile 2> $mergeDiagnostics
    if ($LASTEXITCODE -ne 0) { throw 'Profile merge failed' }
    $arguments = @('export', $executables[0], ('-instr-profile=' + $profile))
    foreach ($binary in $executables | Select-Object -Skip 1) { $arguments += @('-object', $binary) }
    $arguments += @(Get-ChildItem -LiteralPath src -Filter '*.rs' | ForEach-Object FullName)
    $exportDiagnostics = Join-Path $profileRoot 'llvm-cov.stderr.txt'
    $export = & $llvmCov @arguments 2> $exportDiagnostics
    if ($LASTEXITCODE -ne 0) { throw 'Coverage export failed' }
    $diagnostics = @(
        Get-Content -LiteralPath $mergeDiagnostics -ErrorAction SilentlyContinue
        Get-Content -LiteralPath $exportDiagnostics -ErrorAction SilentlyContinue
    ) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
    foreach ($diagnostic in $diagnostics) { Write-Warning "LLVM coverage diagnostic: $diagnostic" }
    $data = ($export -join "`n") | ConvertFrom-Json
    $totals = $data.data[0].totals
    if (-not $totals.lines -or $totals.lines.count -le 0) { throw 'LLVM export contains no measured source lines.' }
    $export | Set-Content -LiteralPath (Join-Path $profileRoot 'llvm-coverage.json') -Encoding utf8
    $summary = [ordered]@{
        tool = 'rustc instrumentation and matching LLVM tools'; rustc = (rustc --version)
        coverage_toolchain = $selected; llvm_tools = $toolVersions
        llvm_diagnostics = @($diagnostics)
        source_commit = (git rev-parse HEAD)
        source_dirty = [bool](git status --porcelain)
        source_hashes = @{}
        scope = 'owned src/*.rs; test and dependency files excluded'
        lines = $totals.lines; functions = $totals.functions; regions = $totals.regions
        branch_coverage = $null
        limitations = @('Stable source instrumentation does not provide qualified branch metrics here.',
            '.NET, vendor library internals, GPU and native plugin coverage are not measured.',
            'Sparse CPU adapters are tested through owned wrappers; vendor internals are excluded. GPU/native plugins remain unimplemented.')
    }
    if (@($diagnostics).Count -gt 0) {
        $summary.limitations += 'LLVM emitted coverage diagnostics (see llvm_diagnostics and retained stderr); exported totals may omit mismatched function mappings and are not qualified complete coverage.'
    }
    foreach ($source in @(Get-ChildItem -LiteralPath src -Filter '*.rs') + @(Get-ChildItem -LiteralPath tests -Filter '*.rs')) {
        $relative = [IO.Path]::GetRelativePath($root, $source.FullName).Replace('\','/')
        $summary.source_hashes[$relative] = (Get-FileHash -LiteralPath $source.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    }
    $summary | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath evidence/rust-coverage.json -Encoding utf8
    $summary | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $profileRoot 'summary.json') -Encoding utf8
    $summary | ConvertTo-Json -Depth 8
} finally {
    $env:RUSTFLAGS = $oldFlags; $env:CARGO_TARGET_DIR = $oldTarget; $env:LLVM_PROFILE_FILE = $oldProfile
    $env:RUSTUP_TOOLCHAIN = $oldToolchain
}


