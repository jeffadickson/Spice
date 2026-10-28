# Integration regression: missing LLVM tools must fail before any instrumented build.
# Run only on a disposable development/CI toolchain; every renamed tool is restored.
$ErrorActionPreference = 'Stop'
$coverage = Join-Path $PSScriptRoot 'Test-Coverage.ps1'
$oldToolchain = $env:RUSTUP_TOOLCHAIN
$oldOverride = $env:SPICE_COVERAGE_TOOLCHAIN
try {
    $env:RUSTUP_TOOLCHAIN = $null
    $env:SPICE_COVERAGE_TOOLCHAIN = $null
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
        } finally {
            Move-Item -LiteralPath $hidden -Destination $tool -ErrorAction Stop
        }
    }
    & $coverage -CheckPrerequisitesOnly
    Write-Host 'Coverage prerequisite regressions passed; no coverage measurements were collected by this check.'
} finally {
    $env:RUSTUP_TOOLCHAIN = $oldToolchain
    $env:SPICE_COVERAGE_TOOLCHAIN = $oldOverride
}
