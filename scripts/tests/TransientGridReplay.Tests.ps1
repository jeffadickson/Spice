BeforeAll {$scriptPath=Join-Path $PSScriptRoot '../Compare-TransientGridReplay.ps1'}
Describe 'Transient replay evidence admission' {
    BeforeEach {
        if(!(Test-Path -LiteralPath $TestDrive -PathType Container)){throw 'Missing test root'}
        $baseline=Join-Path $TestDrive ([guid]::NewGuid().ToString('N'))
        New-Item -ItemType Directory -Path $baseline|Out-Null
        $engine=Join-Path $baseline 'unit-test-engine.txt'
        Set-Content -LiteralPath $engine 'Not executable: these tests must refuse before dispatch'
        $output=Join-Path $baseline 'output'
    }
    It 'does not dispatch without a source provenance gate' {
        @{state='completed_with_results';provenance_valid=$false}|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $baseline 'summary.json')
        {& $scriptPath -ManagedBin $baseline -Engine $engine -BaselineDirectory $baseline -OutputDirectory $output}|Should -Throw '*immutable-source*'
        Test-Path -LiteralPath $output|Should -BeFalse
    }
    It 'does not call budget exhaustion a completed baseline' {
        @{state='budget_exhausted';provenance_valid=$true}|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $baseline 'summary.json')
        {& $scriptPath -ManagedBin $baseline -Engine $engine -BaselineDirectory $baseline -OutputDirectory $output}|Should -Throw '*immutable-source*'
        Test-Path -LiteralPath $output|Should -BeFalse
    }
    It 'refuses a different engine before loading assemblies' {
        $hashes=@{};$hashes[[IO.Path]::GetFullPath($engine)]='wrong'
        @{state='completed_with_results';provenance_valid=$true;source_hashes=$hashes}|ConvertTo-Json -Depth 5|Set-Content -LiteralPath (Join-Path $baseline 'summary.json')
        {& $scriptPath -ManagedBin $baseline -Engine $engine -BaselineDirectory $baseline -OutputDirectory $output}|Should -Throw '*engine differs*'
        Test-Path -LiteralPath $output|Should -BeFalse
    }
    It 'compares every observable in an immutable real replay' -Skip:(!$env:SPICE_REFERENCE_GRID_MANAGED -or !$env:SPICE_REFERENCE_GRID_BASELINE -or !$env:SPICE_REFERENCE_GRID_ENGINE) {
        & $scriptPath -ManagedBin $env:SPICE_REFERENCE_GRID_MANAGED -Engine $env:SPICE_REFERENCE_GRID_ENGINE -BaselineDirectory (Join-Path $env:SPICE_REFERENCE_GRID_BASELINE 'baseline-lu') -OutputDirectory $output
        $result=Get-Content -LiteralPath (Join-Path $output 'summary.json') -Raw|ConvertFrom-Json
        $result.runtime_provenance_valid|Should -BeTrue
        $result.results.Count|Should -Be 2
        @($result.results|Where-Object {!$_.agreed}).Count|Should -Be 0
    }
    It 'refuses managed evidence from a different baseline build' -Skip:(!$env:SPICE_REFERENCE_GRID_OLD_MANAGED -or !$env:SPICE_REFERENCE_GRID_BASELINE -or !$env:SPICE_REFERENCE_GRID_ENGINE) {
        {& $scriptPath -ManagedBin $env:SPICE_REFERENCE_GRID_OLD_MANAGED -Engine $env:SPICE_REFERENCE_GRID_ENGINE -BaselineDirectory (Join-Path $env:SPICE_REFERENCE_GRID_BASELINE 'baseline-lu') -OutputDirectory $output}|Should -Throw '*differs from baseline*'
        Test-Path -LiteralPath (Join-Path $output 'summary.json')|Should -BeFalse
    }
    It 'refuses a changed archived request before simulation' -Skip:(!$env:SPICE_REFERENCE_GRID_MANAGED -or !$env:SPICE_REFERENCE_GRID_BASELINE -or !$env:SPICE_REFERENCE_GRID_ENGINE) {
        $copy=Join-Path $baseline 'changed-baseline'
        Copy-Item -LiteralPath (Join-Path $env:SPICE_REFERENCE_GRID_BASELINE 'baseline-lu') -Destination $copy -Recurse
        $request=Join-Path $copy 'cases/0000/requested_2/00000.request.json'
        $data=Get-Content -LiteralPath $request -Raw|ConvertFrom-Json
        $data.analysis.options.max_steps++
        $data|ConvertTo-Json -Depth 50|Set-Content -LiteralPath $request
        {& $scriptPath -ManagedBin $env:SPICE_REFERENCE_GRID_MANAGED -Engine $env:SPICE_REFERENCE_GRID_ENGINE -BaselineDirectory $copy -OutputDirectory $output}|Should -Throw '*archived baseline request*'
        Test-Path -LiteralPath (Join-Path $output 'summary.json')|Should -BeFalse
    }
}
