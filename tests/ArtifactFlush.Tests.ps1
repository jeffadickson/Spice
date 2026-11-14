BeforeAll {
    $root=Split-Path -Parent $PSScriptRoot
    $probe=Join-Path $root scripts/Probe-FileFlush.ps1
    $coverage=Join-Path $root scripts/Measure-RustIntegrationCoverage.ps1
    $artifactRoot=if($env:CODEX_ARTIFACT_ROOT){$env:CODEX_ARTIFACT_ROOT}else{'E:/Artifacts'}
    if(!(Test-Path -LiteralPath $artifactRoot -PathType Container)){throw 'Artifact root missing'}
    $probeParent=Join-Path $artifactRoot 'Spice-flush-tests'
    if(!(Test-Path -LiteralPath $probeParent -PathType Container)){New-Item -ItemType Directory -Path $probeParent|Out-Null}
}
Describe 'Equivalent flush diagnostic and coverage working-directory boundaries' {
    It 'refuses an existing diagnostic output without overwriting it' {
        {& $probe -OutputDirectory $TestDrive}|Should -Throw '*Fresh diagnostic*'
    }
    It 'refuses a missing diagnostic parent before compiling' {
        {& $probe -OutputDirectory (Join-Path $TestDrive 'missing/child')}|Should -Throw '*Missing*'
    }
    It 'refuses a missing test working directory before coverage output creation' {
        $destination=Join-Path $TestDrive 'coverage-never-created'
        {& $coverage -Repository $root -TargetDirectory $TestDrive -Output $destination -WorkingDirectory (Join-Path $TestDrive absent)}|Should -Throw '*Missing test working directory*'
        Test-Path -LiteralPath $destination|Should -BeFalse
    }
    It 'runs the pinned standard library diagnostic and reports bounded observations' {
        # Pester's default TestDrive may be on C:. The compiler/diagnostic's
        # actual output is a fresh E: child even for a direct Invoke-Pester call.
        $destination=Join-Path $probeParent ([guid]::NewGuid().ToString('N'))
        & $probe -OutputDirectory $destination|Out-Null
        $result=Get-Content -LiteralPath (Join-Path $destination result.json) -Raw|ConvertFrom-Json
        $result.result.cases|Should -Be 4
        $result.result.pre_flush_visibility|Should -BeTrue
        $result.result.post_flush_unchanged|Should -BeTrue
        $result.result.sync_all_retained|Should -BeTrue
        $result.source_sha256|Should -Match '^[a-f0-9]{64}$'
    }
}
