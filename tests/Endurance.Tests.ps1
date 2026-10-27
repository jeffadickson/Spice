# Pester contracts exercise process-boundary rejection without starting long work.
Describe 'Endurance launcher validation' {
    It 'does not extend the campaign across independent UTC or monotonic changes' {
        & dotnet run --file (Join-Path $PSScriptRoot '../scripts/Endurance.cs') -- --budget-self-test
        $LASTEXITCODE|Should Be 0
    }
    It 'releases a temporary Windows system-required request' {
        & dotnet run --file (Join-Path $PSScriptRoot '../scripts/Endurance.cs') -- --power-probe
        $LASTEXITCODE|Should Be 0
    }
    $launcher=Join-Path $PSScriptRoot '../scripts/Run-Endurance.ps1'
    It 'rejects invalid or oversized wall-clock budgets' {
        foreach ($minutes in @('0','-1','1441','NaN')) {
            & pwsh -NoProfile -File $launcher -Minutes $minutes *> $null
            $LASTEXITCODE | Should Not Be 0
        }
    }
    It 'rejects nonnumeric seeds before launching a process' {
        & pwsh -NoProfile -File $launcher -Seed 'bad-seed' *> $null
        $LASTEXITCODE | Should Not Be 0
    }
    It 'rejects conflicting mutation switches' {
        & pwsh -NoProfile -File $launcher -SkipMutation -SelectedRegressions *> $null
        $LASTEXITCODE | Should Not Be 0
    }
}
