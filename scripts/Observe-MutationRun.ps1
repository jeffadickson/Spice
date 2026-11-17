param(
 [Parameter(Mandatory)][string]$Script,
 [Parameter(Mandatory)][string]$RunDirectory,
 [ValidateRange(1,86400)][int]$TimeoutSeconds=14400,
 [ValidateRange(1,60)][int]$SampleSeconds=15
)
# Mutation-only observation, not a host memory governor. Windows owns paging.
# Private-byte usage has no portable change event, so sample every 15s by default (configurable for bounded tests).
$ErrorActionPreference='Stop'
if(-not $IsWindows){throw 'This process observer is qualified on Windows only'}
if(!(Test-Path -LiteralPath $Script -PathType Leaf)){throw 'Mutation script missing'}
if(!(Test-Path -LiteralPath $RunDirectory -PathType Container)){throw 'Observation directory missing'}
$helpers=Join-Path $PSScriptRoot 'MutationObservation.ps1'
if(!(Test-Path -LiteralPath $helpers -PathType Leaf)){throw 'Observation helpers missing'}
. $helpers
$log=Join-Path $RunDirectory 'memory-observation.jsonl'
if(Test-Path -LiteralPath $log){throw 'Fresh observation output required'}
$env:DOTNET_CLI_USE_MSBUILD_SERVER='0';$env:MSBUILDDISABLENODEREUSE='1'
$child=Start-Process -FilePath (Get-Process -Id $PID).Path -ArgumentList @('-NoProfile','-File',('"'+$Script+'"')) -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $RunDirectory 'stdout.log') -RedirectStandardError (Join-Path $RunDirectory 'stderr.log')
$owned=@{};$owned[$child.Id]=$child;$clock=[Diagnostics.Stopwatch]::StartNew()
@{state='started';pid=$child.Id;started_utc=$child.StartTime.ToUniversalTime().ToString('o');memory_policy='observe_only_no_caps_no_free_RAM_gate';timeout_seconds=$TimeoutSeconds}|ConvertTo-Json -Compress|Add-Content -LiteralPath $log
try {
 $completion=$child.WaitForExitAsync()
 while(!$completion.IsCompleted){
  $snapshot=@(Get-CimInstance Win32_Process)
  # Discover lineage while a retained parent identity is alive. Retained Process
  # handles distinguish already observed workers from later PID reuse.
  do {
   $added=$false
   foreach($item in $snapshot){
    if(!$owned.ContainsKey([int]$item.ProcessId) -and $owned.ContainsKey([int]$item.ParentProcessId)){
     $parent=$owned[[int]$item.ParentProcessId]
     if(!$parent.HasExited -and $item.CreationDate.ToUniversalTime() -ge $parent.StartTime.ToUniversalTime()){
      try{
       $p=Get-Process -Id $item.ProcessId -ErrorAction Stop;$null=$p.Handle
       # CIM timestamps expose microseconds; Process uses 100ns ticks. Refuse
       # acquisition if identity differs beyond that documented precision.
       if([Math]::Abs($p.StartTime.ToUniversalTime().Ticks-$item.CreationDate.ToUniversalTime().Ticks)-le 10){$owned[$p.Id]=$p;$added=$true}else{$p.Dispose()}
      }catch [ArgumentException]{}catch [Microsoft.PowerShell.Commands.ProcessCommandException]{}catch [InvalidOperationException]{}
     }
    }
   }
  } while($added)
  $usage=@(foreach($p in $owned.Values){$observation=Get-MutationWorkerObservation -Process $p;if($null-ne $observation){$observation}})
  @{state='observed';utc=[DateTimeOffset]::UtcNow.ToString('o');elapsed_seconds=$clock.Elapsed.TotalSeconds;workers=$usage}|ConvertTo-Json -Depth 4 -Compress|Add-Content -LiteralPath $log
  if($clock.Elapsed.TotalSeconds -ge $TimeoutSeconds){
   @{state='deadline_termination';utc=[DateTimeOffset]::UtcNow.ToString('o');pid=$child.Id}|ConvertTo-Json -Compress|Add-Content -LiteralPath $log
   $child.Kill($true);$child.WaitForExit();throw 'Mutation run deadline reached; incomplete outcomes remain incomplete'
  }
  $wake=[Threading.CancellationTokenSource]::new()
  try {
   $delay=[Threading.Tasks.Task]::Delay([TimeSpan]::FromSeconds([Math]::Max(0.01,[Math]::Min($SampleSeconds,$TimeoutSeconds-$clock.Elapsed.TotalSeconds))),$wake.Token)
   [Threading.Tasks.Task]::WhenAny([Threading.Tasks.Task[]]@($completion,$delay)).GetAwaiter().GetResult()|Out-Null
  } finally {
   $wake.Cancel();try{$delay.GetAwaiter().GetResult()}catch{if(!$wake.IsCancellationRequested){throw}};$wake.Dispose()
  }
 }
 $completion.GetAwaiter().GetResult()
 $exit=$child.ExitCode
 @{state='finished';exit=$exit;elapsed_seconds=$clock.Elapsed.TotalSeconds}|ConvertTo-Json -Compress|Add-Content -LiteralPath $log
 exit $exit
} finally {
 if(!$child.HasExited){$child.Kill($true);$child.WaitForExit()}
 foreach($p in $owned.Values){
  try {
   if(!$p.HasExited){
    @{state='descendant_cleanup';pid=$p.Id;started_utc=$p.StartTime.ToUniversalTime().ToString('o');reason='owning_root_finished'}|ConvertTo-Json -Compress|Add-Content -LiteralPath $log
    $p.Kill($true);$p.WaitForExitAsync().WaitAsync([TimeSpan]::FromSeconds(5)).GetAwaiter().GetResult()|Out-Null
   }
  }catch [InvalidOperationException]{}finally{$p.Dispose()}
 }
}
