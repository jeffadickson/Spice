# Integration probe: hold the live checkpoint without delete sharing, as ordinary
# Windows readers may do. The supervisor must retry, then finish with intact JSON.
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
Set-Location $root
$stdout=Join-Path $root 'build/checkpoint-contention.out.log'
$stderr=Join-Path $root 'build/checkpoint-contention.err.log'
$process=Start-Process -FilePath (Get-Command dotnet).Source -ArgumentList @('run','--file','scripts/Endurance.cs','--','--minutes','1','--seed','329','--skip-mutation') -WorkingDirectory $root -WindowStyle Hidden -PassThru -RedirectStandardOutput $stdout -RedirectStandardError $stderr
$deadline=[DateTime]::UtcNow.AddSeconds(55);$summary=$null
while([DateTime]::UtcNow -lt $deadline -and -not $process.HasExited) {
    if(Test-Path $stdout){
        $line=Get-Content $stdout|Where-Object{$_ -like 'Campaign summary:*'}|Select-Object -First 1
        if($line){$summary=$line.Substring('Campaign summary: '.Length);if(Test-Path $summary){$r=Get-Content -Raw $summary|ConvertFrom-Json;if($r.phase -eq 'seeded_batches'){break}}}
    }
    Start-Sleep -Milliseconds 100
}
if(-not $summary -or $process.HasExited){throw 'Worker did not reach checkpoint probe'}
$stream=[IO.File]::Open($summary,[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::Read)
try{Start-Sleep -Milliseconds 500}finally{$stream.Dispose()}
if(-not $process.WaitForExit(90000)){throw 'Checkpoint probe worker exceeded deadline'}
$final=Get-Content -Raw $summary|ConvertFrom-Json
if($process.ExitCode -ne 0 -or $final.status -notlike 'budget_exhausted*' -or $final.cases -eq 0 -or $final.checkpoint_retries -eq 0){throw ('Checkpoint contention probe failed: '+($final|ConvertTo-Json -Depth 2))}
$final|Select-Object status,cases,checkpoint_retries,elapsed_seconds|ConvertTo-Json|Set-Content evidence/checkpoint-contention.json
Write-Output ('Checkpoint contention recovered; retries='+$final.checkpoint_retries+', cases='+$final.cases)
