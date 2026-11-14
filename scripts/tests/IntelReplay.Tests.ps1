BeforeAll {
    $compare=Join-Path $PSScriptRoot '../Compare-IntelReplay.ps1'
    function New-ReplayFixture([string]$mode) {
        if(!(Test-Path -LiteralPath $TestDrive -PathType Container)){throw 'Missing test parent'}
        $directory=Join-Path $TestDrive ([guid]::NewGuid().ToString('N'))
        New-Item -ItemType Directory -Path $directory|Out-Null
        $rows=@()
        foreach($backend in @('lu','intel')){
            $identity=if($backend -eq 'lu'){'nalgebra_dense_lu_reference'}else{'intel_opencl_viennacl_gmres_experimental'}
            $response=@{case_id='physical';complete=$true;status='succeeded';backend=@{id=$identity;gpu=($backend -eq 'intel');placement=$(if($backend -eq 'intel'){'gpu'}else{'cpu'})};signals=@(@{id='v(out)';quantity='voltage';unit='V'},@{id='i(V1)';quantity='current';unit='A'});samples=@(@{time_s=0.;values=@(5.,-0.005)});ac_samples=@()}
            if($backend -eq 'intel'){
                switch($mode){
                    'wrong_value' {$response.samples[0].values[0]=6.}
                    'missing_value' {$response.samples[0].values=@(5.)}
                    'cpu_fallback' {$response.backend.gpu=$false;$response.backend.placement='cpu'}
                    'different_grid' {$response.samples[0].time_s=1e-6}
                    'missing_axis' {$response.samples[0].Remove('time_s')}
                    'complex' {$response.ac_samples=@(@{frequency_hz=100.;values=@(@{real=5.;imag=-1.},@{real=-0.005;imag=0.001})});$response.samples=@()}
                }
            } elseif($mode -eq 'complex') {
                $response.ac_samples=@(@{frequency_hz=100.;values=@(@{real=5.;imag=-1.},@{real=-0.005;imag=0.001})});$response.samples=@()
            }
            if($mode -eq 'empty_signals'){$response.signals=@();$response.samples[0].values=@()}
            if($mode -eq 'mixed_analysis'){
                $response.ac_samples=@(@{frequency_hz=100.;values=@(@{real=5.;imag=-1.},@{real=-0.005;imag=0.001})})
                if($backend -eq 'intel'){$response.samples[0].values[0]=6.}
            }
            $file=$backend+'.json'
            @{stdout=($response|ConvertTo-Json -Depth 20 -Compress)}|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $directory $file)
            $rows+=@{case=0;backend=$backend;warmup=$false;success=$true;iteration=1;artifact=$file;request_sha256=$(if($mode -eq 'different_request' -and $backend -eq 'intel'){'different-input'}else{'same-input'})}
        }
        @{rows=$rows;runtime_provenance_valid=($mode -ne 'invalid_provenance')}|ConvertTo-Json -Depth 8|Set-Content -LiteralPath (Join-Path $directory 'summary.json')
        & $compare -MeasurementDirectory $directory
        (Get-Content -LiteralPath (Join-Path $directory 'cpu-agreement.json') -Raw|ConvertFrom-Json).results[0]
    }
}
Describe 'Complete Intel response comparison' {
    It 'accepts complete identical real signals' {(New-ReplayFixture 'valid').state|Should -Be 'agreed'}
    It 'compares both AC quadratures' {(New-ReplayFixture 'complex').state|Should -Be 'agreed'}
    It 'detects a physically wrong voltage' {(New-ReplayFixture 'wrong_value').state|Should -Be 'numerical_disagreement'}
    It 'refuses missing values instead of substituting zero' {(New-ReplayFixture 'missing_value').state|Should -Be 'unavailable_evidence'}
    It 'refuses a CPU fallback labelled Intel' {(New-ReplayFixture 'cpu_fallback').state|Should -Be 'unavailable_evidence'}
    It 'does not call different grids agreeing' {(New-ReplayFixture 'different_grid').state|Should -Be 'unavailable_evidence'}
    It 'does not replace an unknown time with zero' {(New-ReplayFixture 'missing_axis').state|Should -Be 'unavailable_evidence'}
    It 'refuses an empty observable set' {(New-ReplayFixture 'empty_signals').state|Should -Be 'unavailable_evidence'}
    It 'refuses invalid runtime provenance' {(New-ReplayFixture 'invalid_provenance').state|Should -Be 'unavailable_evidence'}
    It 'refuses different request hashes' {(New-ReplayFixture 'different_request').state|Should -Be 'unavailable_evidence'}
    It 'does not ignore differing real samples beside AC data' {(New-ReplayFixture 'mixed_analysis').state|Should -Be 'unavailable_evidence'}
}
