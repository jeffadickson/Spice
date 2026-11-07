BeforeAll {
    $script:probe = Join-Path $PSScriptRoot '../Run-CuDssProbe.ps1'
    if (!(Test-Path -LiteralPath $script:probe -PathType Leaf)) {
        throw 'Missing cuDSS probe script.'
    }
}

Describe 'cuDSS workstation preflight' {
    It 'returns unavailable and preserves the exact sample pin when tools are missing' {
        Mock Get-Command {} -ParameterFilter { $CommandType -eq 'Application' }
        $json = & $script:probe -CudssConfig $PSScriptRoot -CudssRuntime $PSScriptRoot -PreflightOnly
        $exitCode = $LASTEXITCODE
        $report = $json | ConvertFrom-Json
        $exitCode | Should -Be 2
        $report.status | Should -Be 'unavailable'
        $report.missing | Should -Contain 'nvcc'
        $report.sample_commit | Should -Be '8b0573b11d183d9b3a36cd2955aa31c16cc61b95'
    }

    It 'returns unavailable when an adapter query executable rejects the request' {
        # The current PowerShell executable rejects nvidia-smi's GPU query
        # arguments. This exercises native process and exit handling on any host.
        Mock Get-Command { @{ Source = (Get-Process -Id $PID).Path } } -ParameterFilter { $CommandType -eq 'Application' }
        $json = & $script:probe -CudssConfig $PSScriptRoot -CudssRuntime $PSScriptRoot -PreflightOnly
        $exitCode = $LASTEXITCODE
        $report = $json | ConvertFrom-Json
        $exitCode | Should -Be 2
        $report.status | Should -Be 'unavailable'
        $report.commands.adapter.exit_code | Should -Not -Be 0
        $report.commands.adapter.timed_out | Should -BeFalse
    }
}
