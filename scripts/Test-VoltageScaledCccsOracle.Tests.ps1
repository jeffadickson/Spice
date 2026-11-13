# Explicit native/oracle integration: ordinary test discovery stays inert until
# the caller supplies isolated artifacts and qualified executable/assembly paths.
$configured=$env:SPICE_CPP_ORACLE_ENGINE -and $env:SPICE_CPP_ORACLE_NGSPICE -and
    $env:SPICE_CPP_ORACLE_MANAGED_BIN -and $env:SPICE_CPP_ORACLE_OUTPUT_PARENT
Describe 'Voltage-scaled CCCS public client and stock oracle' -Skip:(!$configured) {
    BeforeAll {
        $script:repo=(Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
        $script:output=Join-Path $env:SPICE_CPP_ORACLE_OUTPUT_PARENT ('pester-oracle-'+[guid]::NewGuid().ToString('N'))
        & (Join-Path $PSScriptRoot 'Test-VoltageScaledCccsOracle.ps1') -Repository $script:repo -Output $script:output `
            -ManagedBin $env:SPICE_CPP_ORACLE_MANAGED_BIN -Engine $env:SPICE_CPP_ORACLE_ENGINE -Ngspice $env:SPICE_CPP_ORACLE_NGSPICE
        if($LASTEXITCODE){throw 'Authored oracle helper failed'}
        $script:summary=Get-Content -LiteralPath (Join-Path $script:output 'authored-oracle.json') -Raw|ConvertFrom-Json
        Set-Content -LiteralPath (Join-Path $env:SPICE_CPP_ORACLE_OUTPUT_PARENT 'pester-oracle-output.txt') -Value $script:output
    }
    It 'agrees on every observer for DC and each independently driven AC control on both providers' {
        @($script:summary.rows).Count|Should -Be 6
        @($script:summary.rows|Where-Object {!$_.native_agreed -or $_.observables.Count -ne 5}).Count|Should -Be 0
        $script:summary.semantic_failure.status|Should -Be 'failed'
        $script:summary.semantic_failure.complete|Should -BeFalse
        $script:summary.valid_after_failure|Should -BeTrue
    }
    It 'refuses reused output without changing its successful evidence' {
        $path=Join-Path $script:output 'authored-oracle.json'
        $before=(Get-FileHash -LiteralPath $path).Hash
        {& (Join-Path $PSScriptRoot 'Test-VoltageScaledCccsOracle.ps1') -Repository $script:repo -Output $script:output `
            -ManagedBin $env:SPICE_CPP_ORACLE_MANAGED_BIN -Engine $env:SPICE_CPP_ORACLE_ENGINE -Ngspice $env:SPICE_CPP_ORACLE_NGSPICE}|
            Should -Throw '*Fresh authored-oracle output required*'
        (Get-FileHash -LiteralPath $path).Hash|Should -Be $before
    }
}
