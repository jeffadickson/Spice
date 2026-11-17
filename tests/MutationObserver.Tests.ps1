Describe 'Mutation-only process observation' {
    BeforeAll {
        $repo=Split-Path $PSScriptRoot -Parent
        $root=$env:CODEX_ARTIFACT_ROOT
        if(!$root -or !(Test-Path -LiteralPath $root -PathType Container)){throw 'Existing artifact root required'}
        $observer=Join-Path $repo 'scripts/Observe-MutationRun.ps1'
        . (Join-Path $repo 'scripts/MutationObservation.ps1')
        if(-not ('MutationExitingProbe' -as [type])){
            Add-Type @'
public sealed class MutationExitingProbe {
 public bool HasExited => false;
 public void Refresh() => throw new System.InvalidOperationException("Process exited between reads");
}
'@
        }
    }
    It 'propagates a completed worker outcome without imposing memory caps' -Skip:(-not $IsWindows) {
        $directory=Join-Path $root ('observer-contract-'+[guid]::NewGuid().ToString('N'))
        New-Item -ItemType Directory -Path $directory|Out-Null
        $fixture=Join-Path $directory 'worker.ps1';'exit 7'|Set-Content -LiteralPath $fixture
        & (Get-Process -Id $PID).Path -NoProfile -File $observer -Script $fixture -RunDirectory $directory -TimeoutSeconds 15 *> (Join-Path $directory 'observer.log')
        $LASTEXITCODE|Should -Be 7
        $events=@(Get-Content -LiteralPath (Join-Path $directory 'memory-observation.jsonl')|ForEach-Object{$_|ConvertFrom-Json})
        $events[0].memory_policy|Should -Be 'observe_only_no_caps_no_free_RAM_gate'
        $events[-1].state|Should -Be 'finished';$events[-1].exit|Should -Be 7
    }
    It 'stops its own overdue worker and records incomplete evidence' -Skip:(-not $IsWindows) {
        $directory=Join-Path $root ('observer-deadline-'+[guid]::NewGuid().ToString('N'))
        New-Item -ItemType Directory -Path $directory|Out-Null
        $fixture=Join-Path $directory 'worker.ps1';'Start-Sleep -Seconds 60'|Set-Content -LiteralPath $fixture
        & (Get-Process -Id $PID).Path -NoProfile -File $observer -Script $fixture -RunDirectory $directory -TimeoutSeconds 1 *> (Join-Path $directory 'observer.log')
        $LASTEXITCODE|Should -Not -Be 0
        $events=@(Get-Content -LiteralPath (Join-Path $directory 'memory-observation.jsonl')|ForEach-Object{$_|ConvertFrom-Json})
        @($events|Where-Object state -eq 'deadline_termination').Count|Should -Be 1
        @($events|Where-Object state -eq 'finished').Count|Should -Be 0
    }
    It 'treats an exit between process-property queries as an unavailable sample' {
        $sample=Get-MutationWorkerObservation -Process ([MutationExitingProbe]::new())
        $sample|Should -BeNullOrEmpty
    }
    It 'reaps a retained child after its root fails' -Skip:(-not $IsWindows) {
        $directory=Join-Path $root ('observer-orphan-'+[guid]::NewGuid().ToString('N'))
        New-Item -ItemType Directory -Path $directory|Out-Null
        $fixture=Join-Path $directory 'worker.ps1'
        @'
$child=Start-Process -FilePath (Get-Process -Id $PID).Path -ArgumentList @('-NoProfile','-Command','"Start-Sleep -Seconds 60"') -WindowStyle Hidden -PassThru
@{pid=$child.Id;started=$child.StartTime.ToUniversalTime().ToString('o')}|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $PSScriptRoot 'child.json')
Start-Sleep -Seconds 4
exit 7
'@|Set-Content -LiteralPath $fixture
        & (Get-Process -Id $PID).Path -NoProfile -File $observer -Script $fixture -RunDirectory $directory -TimeoutSeconds 30 -SampleSeconds 1 *> (Join-Path $directory 'observer.log')
        $LASTEXITCODE|Should -Be 7
        $identity=Get-Content -LiteralPath (Join-Path $directory 'child.json') -Raw|ConvertFrom-Json
        $events=@(Get-Content -LiteralPath (Join-Path $directory 'memory-observation.jsonl')|ForEach-Object{$_|ConvertFrom-Json})
        @($events|Where-Object { $_.state -eq 'descendant_cleanup' -and $_.pid -eq $identity.pid }).Count|Should -Be 1
        $process=Get-Process -Id $identity.pid -ErrorAction SilentlyContinue
        if($process){
            try {
                $same=$process.StartTime.ToUniversalTime().ToString('o') -eq $identity.started
                if($same -and !$process.HasExited){$process.Kill($true);$process.WaitForExit();throw 'Retained child escaped observer cleanup'}
            }finally{$process.Dispose()}
        }
    }
}
