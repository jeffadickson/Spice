BeforeAll {
    $launcher=Join-Path $PSScriptRoot '../Test-IntelCircuits.ps1'
}
Describe 'Intel circuit verification orchestration' {
    It 'refuses a missing provider without creating a target' {
        $target=Join-Path $TestDrive 'missing-provider-target'
        {& $launcher -Provider '' -ArtifactRoot $TestDrive -TargetDirectory $target}|Should -Throw '*SPICE_INTEL_HOST*'
        Test-Path -LiteralPath $target|Should -BeFalse
    }
    It 'does not count a failed circuit test as passing and restores output settings' {
        $provider=Join-Path $TestDrive 'mock-provider.dll';Set-Content -LiteralPath $provider 'mock launcher unit-test data; not executable'
        $target=Join-Path $TestDrive 'failed-target'
        Mock cargo {$global:LASTEXITCODE=17}
        Mock rustc {'mock rustc for launcher unit tests'}
        $old=$env:CARGO_TARGET_DIR;$oldTemp=$env:TEMP
        {& $launcher -Provider $provider -ArtifactRoot $TestDrive -TargetDirectory $target}|Should -Throw '*17*'
        $env:CARGO_TARGET_DIR|Should -Be $old
        $env:TEMP|Should -Be $oldTemp
        Should -Invoke cargo -Times 1 -Exactly -ParameterFilter {($args -join ' ') -match '--features intel-opencl' -and ($args -join ' ') -match '--test intel_backend'}
        $summary=Get-ChildItem -LiteralPath $TestDrive -Directory -Filter 'Spice-intel-circuit-tests-*'|ForEach-Object {Get-Content -LiteralPath (Join-Path $_.FullName 'summary.json') -Raw|ConvertFrom-Json}
        @($summary|Where-Object exit_code -eq 17).Count|Should -Be 1
    }
    It 'refuses a shared active test cache instead of overlapping verification' {
        $provider=Join-Path $TestDrive 'lock-provider.dll';Set-Content -LiteralPath $provider 'mock data'
        $target=Join-Path $TestDrive 'locked-target';New-Item -ItemType Directory -Path $target|Out-Null
        $lock=[IO.File]::Open((Join-Path $target 'intel-verification.lock'),'OpenOrCreate','ReadWrite','None')
        try {{& $launcher -Provider $provider -ArtifactRoot $TestDrive -TargetDirectory $target}|Should -Throw}
        finally {$lock.Dispose()}
    }
    It 'runs full Intel circuit verification from the normal numerical entry point' {
        Get-Content -LiteralPath (Join-Path $PSScriptRoot '../Test.ps1') -Raw|Should -Match "Test-IntelCircuits.ps1'\)"
    }
}
