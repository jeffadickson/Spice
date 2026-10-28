# Integration regression: missing LLVM tools must fail before any instrumented build.
# Run only on a disposable development/CI toolchain; every renamed tool is restored.
$ErrorActionPreference = 'Stop'
$coverage = Join-Path $PSScriptRoot 'Test-Coverage.ps1'
$oldToolchain = $env:RUSTUP_TOOLCHAIN
$oldOverride = $env:SPICE_COVERAGE_TOOLCHAIN
try {
    Remove-Item Env:RUSTUP_TOOLCHAIN -ErrorAction SilentlyContinue
    Remove-Item Env:SPICE_COVERAGE_TOOLCHAIN -ErrorAction SilentlyContinue
    & $coverage -CheckPrerequisitesOnly
    $sysroot = (rustc --print sysroot).Trim()
    if ($LASTEXITCODE -ne 0) { throw 'Cannot resolve regression sysroot.' }
    $hostTarget = ((rustc -vV | Select-String '^host:').ToString()).Substring(6).Trim()
    if ($LASTEXITCODE -ne 0) { throw 'Cannot resolve regression host.' }
    $extension = if ($IsWindows) { '.exe' } else { '' }
    foreach ($name in @('llvm-profdata', 'llvm-cov')) {
        $tool = Join-Path $sysroot ("lib/rustlib/$hostTarget/bin/$name" + $extension)
        $hidden = $tool + '.spice-prerequisite-test'
        Move-Item -LiteralPath $tool -Destination $hidden -ErrorAction Stop
        try {
            $failure = $null
            try { & $coverage -CheckPrerequisitesOnly } catch { $failure = $_.Exception.Message }
            if (-not $failure -or -not $failure.Contains("Coverage prerequisite missing: $tool") -or
                -not $failure.Contains('rustup component add llvm-tools-preview --toolchain')) {
                throw "Missing-tool regression failed for $name : $failure"
            }
            if ($env:RUSTUP_TOOLCHAIN -ne $null) { throw 'Coverage did not restore RUSTUP_TOOLCHAIN after failure.' }
            # A present but invalid executable must also fail the --version probe.
            Set-Content -LiteralPath $tool -Value 'invalid executable fixture' -Encoding utf8
            $failure = $null
            try { & $coverage -CheckPrerequisitesOnly } catch { $failure = $_.Exception.Message }
            if (-not $failure -or -not $failure.Contains("Coverage prerequisite cannot execute: $tool")) {
                throw "Unusable-tool regression failed for $name : $failure"
            }
        } finally {
            Remove-Item -LiteralPath $tool -ErrorAction SilentlyContinue
            Move-Item -LiteralPath $hidden -Destination $tool -ErrorAction Stop
        }
    }
    # An explicit parameter must take precedence over an invalid environment override.
    $env:SPICE_COVERAGE_TOOLCHAIN = 'spice-intentionally-missing-toolchain'
    $selected = ((rustup show active-toolchain) -split '\s+')[0]
    if ($LASTEXITCODE -ne 0) { throw 'Cannot resolve explicit regression toolchain.' }
    & $coverage -CoverageToolchain $selected -CheckPrerequisitesOnly
    Remove-Item Env:SPICE_COVERAGE_TOOLCHAIN -ErrorAction SilentlyContinue
    & $coverage -CheckPrerequisitesOnly
    Write-Host 'Coverage prerequisite regressions passed; no coverage measurements were collected by this check.'
} finally {
    $env:RUSTUP_TOOLCHAIN = $oldToolchain
    $env:SPICE_COVERAGE_TOOLCHAIN = $oldOverride
}
