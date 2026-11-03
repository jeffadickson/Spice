# Pester5 contracts reject invalid input without starting long work. BCL clock
# and power probes execute the compiled C# supervisor's explicit self-test modes.
BeforeAll {
    $script:launcher=Join-Path $PSScriptRoot '../scripts/Run-Endurance.ps1'
    $script:supervisor=Join-Path $PSScriptRoot '../scripts/Endurance.cs'
    $parent=$env:SPICE_SOAK_TEST_ROOT
    if(-not(Test-Path -LiteralPath $parent -PathType Container)){throw 'Set existing SPICE_SOAK_TEST_ROOT on the artifact drive'}
    $script:hostBuild=Join-Path $parent ('endurance-contract-host-'+[guid]::NewGuid().ToString('N'))
}
Describe 'Endurance launcher validation' {
    It 'does not extend campaigns across UTC/monotonic changes or finish-by windows' {
        & dotnet run --file $supervisor --artifacts-path $hostBuild -- --budget-self-test
        $LASTEXITCODE|Should -Be 0
    }
    It 'releases a temporary Windows system-required request' {
        & dotnet run --file $supervisor --artifacts-path $hostBuild -- --power-probe
        $LASTEXITCODE|Should -Be 0
    }
    It 'rejects invalid or oversized wall-clock budgets' {
        foreach ($minutes in @('0','-1','1441','NaN')) {
            {& $launcher -Minutes $minutes}|Should -Throw
        }
    }
    It 'rejects nonnumeric seeds before launching a process' {
        {& $launcher -Seed 'bad-seed'}|Should -Throw
    }
    It 'rejects conflicting mutation switches' {
        {& $launcher -SkipMutation -SelectedRegressions}|Should -Throw
    }
    It 'rejects malformed or expired finish-by deadlines before tool setup' {
        {& $launcher -FinishByUtc 'not-a-date'}|Should -Throw
        {& $launcher -FinishByUtc ([DateTimeOffset]::UtcNow.AddMinutes(-1).ToString('o'))}|Should -Throw '*expired*'
    }
}
