param(
 [Parameter(Mandatory)][string]$Checkout,
 [Parameter(Mandatory)][string]$CanonicalRepository,
 [Parameter(Mandatory)][ValidatePattern('^[0-9a-f]{40}$')][string]$ExpectedCommit,
 [Parameter(Mandatory)][string]$ArtifactDirectory,
 [ValidateRange(0.01,1440)][double]$Minutes=420,
 [switch]$Background,[switch]$ValidateOnly,[switch]$SkipMutation,[string]$FinishByUtc=''
)
# Supervise the existing endurance implementation from a clean pinned checkout.
# The actual Endurance worker owns the canonical lock across all worktrees.
# This launcher takes it provisionally while preparing a fresh run, then releases
# it before dispatch. A concurrent campaign can win admission, but cannot overlap.
$ErrorActionPreference='Stop'
if(-not [double]::IsFinite($Minutes)){throw 'Minutes must be finite'}
if($FinishByUtc){$finish=[DateTimeOffset]::ParseExact($FinishByUtc,'O',[cultureinfo]::InvariantCulture).ToUniversalTime();if($finish -le [DateTimeOffset]::UtcNow){throw 'Finish-by window has expired'};$FinishByUtc=$finish.ToString('o')}
foreach($p in @($Checkout,$CanonicalRepository,(Split-Path -Parent $ArtifactDirectory))){
 if(-not(Test-Path -LiteralPath $p -PathType Container)){throw "Missing directory: $p"}
}
$Checkout=(Resolve-Path -LiteralPath $Checkout).Path
$CanonicalRepository=(Resolve-Path -LiteralPath $CanonicalRepository).Path
$ArtifactDirectory=[IO.Path]::GetFullPath($ArtifactDirectory)
if(-not $IsWindows){throw 'This launcher uses Windows directory junctions; use the native Linux endurance workflow on Linux.'}
$commitOutput=& git -C $Checkout rev-parse HEAD
if($LASTEXITCODE){throw 'Cannot establish verified commit'}
$commit=([string]$commitOutput).Trim();if($commit -ne $ExpectedCommit){throw 'Checkout does not match verified commit'}
$dirty=@(& git -C $Checkout status --porcelain);if($LASTEXITCODE -or $dirty.Count){throw 'Verified soak checkout must be clean'}
$commonOutput=& git -C $Checkout rev-parse --path-format=absolute --git-common-dir
if($LASTEXITCODE){throw 'Cannot establish checkout identity'}
$common=([string]$commonOutput).Trim()
$canonicalOutput=& git -C $CanonicalRepository rev-parse --path-format=absolute --git-common-dir
if($LASTEXITCODE){throw 'Cannot establish canonical identity: different repository or unavailable Git metadata'}
$canonicalCommon=([string]$canonicalOutput).Trim()
if(-not [string]::Equals($common,$canonicalCommon,[StringComparison]::OrdinalIgnoreCase)){throw 'Checkout belongs to a different repository'}
if($ValidateOnly){[pscustomobject]@{State='validated_only';Commit=$commit;Minutes=$Minutes;FinishByUtc=$FinishByUtc;Checkout=$Checkout;Artifacts=$ArtifactDirectory};return}
if($Background){
 if(Test-Path -LiteralPath $ArtifactDirectory){throw 'Fresh artifact directory required'}
 New-Item -ItemType Directory -Path $ArtifactDirectory|Out-Null
 $workerArgs=@('-NoProfile','-File',('"'+$PSCommandPath+'"'),'-Checkout',('"'+$Checkout+'"'),'-CanonicalRepository',('"'+$CanonicalRepository+'"'),'-ExpectedCommit',$ExpectedCommit,'-ArtifactDirectory',('"'+$ArtifactDirectory+'"'),'-Minutes',$Minutes.ToString([cultureinfo]::InvariantCulture))
 if($SkipMutation){$workerArgs+='-SkipMutation'}
 if($FinishByUtc){$workerArgs+=@('-FinishByUtc',$FinishByUtc)}
 $child=Start-Process -FilePath (Get-Command pwsh).Source -ArgumentList $workerArgs -WindowStyle Hidden -WorkingDirectory $Checkout -RedirectStandardOutput (Join-Path $ArtifactDirectory 'launcher.out.log') -RedirectStandardError (Join-Path $ArtifactDirectory 'launcher.err.log') -PassThru
 [pscustomobject]@{State='launcher_started';Pid=$child.Id;Commit=$commit;Artifacts=$ArtifactDirectory};return
}
$work=Join-Path $CanonicalRepository 'work';if(-not(Test-Path -LiteralPath $work -PathType Container)){throw 'Missing canonical work directory'}
$pointer=Join-Path $work 'verified-soak-active.json'
try{$guard=[IO.FileStream]::new((Join-Path $work 'endurance.lock'),[IO.FileMode]::OpenOrCreate,[IO.FileAccess]::ReadWrite,[IO.FileShare]::None)}
catch [IO.IOException]{
 [pscustomobject]@{State='existing_campaign_preserved';CanonicalLock=(Join-Path $work 'endurance.lock');ActivePointer=$(if(Test-Path -LiteralPath $pointer -PathType Leaf){Get-Content -LiteralPath $pointer -Raw}else{$null})};return
}
try{
 # No shared mutable output cache: each pinned run gets its own E: directories.
 if(-not(Test-Path -LiteralPath $ArtifactDirectory -PathType Container)){New-Item -ItemType Directory -Path $ArtifactDirectory|Out-Null}
 # Resolve-Path preserves junction aliases. Resolve every directory component
 # so descendants launch apphosts through the physical artifact location.
 $physicalArtifacts=[IO.Path]::GetPathRoot($ArtifactDirectory)
 foreach($component in $ArtifactDirectory.Substring($physicalArtifacts.Length).Split([char[]]@('\','/'),[StringSplitOptions]::RemoveEmptyEntries)){
  $directory=[IO.DirectoryInfo]::new((Join-Path $physicalArtifacts $component))
  $destination=$directory.ResolveLinkTarget($true)
  $physicalArtifacts=if($null -ne $destination){$destination.FullName}else{$directory.FullName}
 }
 $frameworkWorkRoot=Join-Path $physicalArtifacts 'fm'
 if(Test-Path -LiteralPath $frameworkWorkRoot){throw "Fresh framework mutation output required: $frameworkWorkRoot"}
 New-Item -ItemType Directory -Path $frameworkWorkRoot|Out-Null
 foreach($name in @('work','target')){
  $target=Join-Path $ArtifactDirectory $name;$link=Join-Path $Checkout $name
  if(Test-Path -LiteralPath $link){throw "Fresh checkout output path required: $link"}
  if(Test-Path -LiteralPath $target){throw "Fresh bulk output required: $target"}
  New-Item -ItemType Directory -Path $target|Out-Null
  New-Item -ItemType Junction -Path $link -Target $target|Out-Null
  if(-not(Test-Path -LiteralPath $link -PathType Container)){throw 'Output junction failed'}
 }
 if(-not $SkipMutation){
  # Reuse already pinned installed tools without downloading another copy. This
  # junction is for tool binaries only; run/build outputs remain unique on E:.
  $mutationSource=Join-Path $CanonicalRepository '.tools/mutation'
  if(-not(Test-Path -LiteralPath $mutationSource -PathType Container)){throw 'Pinned canonical mutation tools missing'}
  $tools=Join-Path $Checkout '.tools';if(-not(Test-Path -LiteralPath $tools -PathType Container)){New-Item -ItemType Directory -Path $tools|Out-Null}
  $mutationLink=Join-Path $tools 'mutation';if(Test-Path -LiteralPath $mutationLink){throw 'Fresh mutation tool path required'}
  New-Item -ItemType Junction -Path $mutationLink -Target $mutationSource|Out-Null
 }
 $state=[ordered]@{state='starting';source_commit=$commit;source_dirty=$false;launcher_sha256=(Get-FileHash -LiteralPath $PSCommandPath).Hash.ToLowerInvariant();worker_pid=$PID;checkout=$Checkout;artifact_directory=$ArtifactDirectory;minutes=$Minutes;finish_by_utc=$FinishByUtc;started_utc=[DateTime]::UtcNow.ToString('o');canonical_lock=Join-Path $work 'endurance.lock';limits='Scheduled or started is not completed; inspect actual endurance summary for cases, seeds, mutations and duration.'}
 $state['framework_work_root']=$frameworkWorkRoot
 $state|ConvertTo-Json -Depth 5|Set-Content -LiteralPath $pointer
 $state|ConvertTo-Json -Depth 5|Set-Content -LiteralPath (Join-Path $ArtifactDirectory 'launch-manifest.json')
 . (Join-Path $Checkout 'scripts/HostTools.ps1');Import-SpiceTools -Root $Checkout;Import-SpiceMsvc
 # Anchor both supervisors to one absolute window. The child's bootstrap must
 # consume this window instead of receiving a new Minutes budget on startup.
 $outerStartedUtc=[DateTimeOffset]::UtcNow;$outerClock=[Diagnostics.Stopwatch]::StartNew()
 $effectiveDeadline=$outerStartedUtc.AddMinutes($Minutes)
 if($FinishByUtc){$requestedFinish=[DateTimeOffset]::ParseExact($FinishByUtc,'O',[cultureinfo]::InvariantCulture);if($requestedFinish -lt $effectiveDeadline){$effectiveDeadline=$requestedFinish}}
 $remaining=$effectiveDeadline-$outerStartedUtc
 $state['outer_started_utc']=$outerStartedUtc.ToString('o')
 $state['effective_outer_deadline_utc']=$effectiveDeadline.ToString('o')
 $state['cleanup_reserve_seconds']=5
 if($remaining -le [TimeSpan]::FromSeconds(5)){throw 'Remaining soak window must exceed the five-second cleanup reserve before dispatch.'}
 $testFinish=$effectiveDeadline.AddSeconds(-5)
 $state['test_finish_by_utc']=$testFinish.ToString('o')
 # Invoke a child shell: Run-Endurance.ps1 intentionally exits its own process.
 $arguments=@('-NoProfile','-File',(Join-Path $Checkout 'scripts/Run-Endurance.ps1'),'-Minutes',$Minutes.ToString([cultureinfo]::InvariantCulture));if($SkipMutation){$arguments+='-SkipMutation'}
 # Always apply the earlier effective deadline, including duration-only runs.
 $arguments+=@('-FinishByUtc',$testFinish.ToString('o'))
 $start=[Diagnostics.ProcessStartInfo]::new((Get-Command pwsh).Source);$start.UseShellExecute=$false;$start.CreateNoWindow=$true;$start.WorkingDirectory=$Checkout
 # Scope this export to the owned worker; do not change global environment or
 # derive mutation builds from the checkout/work junction's logical alias.
 $start.Environment['SPICE_FRAMEWORK_WORK_ROOT']=$frameworkWorkRoot
 foreach($argument in $arguments){$start.ArgumentList.Add($argument)}
 $dispatchElapsed=$outerClock.Elapsed;$dispatchUtcElapsed=[DateTimeOffset]::UtcNow-$outerStartedUtc
 if($dispatchUtcElapsed -gt $dispatchElapsed){$dispatchElapsed=$dispatchUtcElapsed}
 if(($remaining-$dispatchElapsed) -le [TimeSpan]::FromSeconds(5)){throw 'Remaining soak window must exceed the five-second cleanup reserve before dispatch.'}
 $guard.Dispose()
 $child=[Diagnostics.Process]::Start($start);if($null -eq $child){throw 'Endurance child did not start'}
 try{
  # Establish child ownership before any reporting that can fail (disk full,
  # checkpoint sharing, etc.). Every exceptional path below terminates the tree.
  $state['child_pid']=$child.Id
  $state|ConvertTo-Json -Depth 5|Set-Content -LiteralPath $pointer
  $state|ConvertTo-Json -Depth 5|Set-Content -LiteralPath (Join-Path $ArtifactDirectory 'launch-manifest.json')
  $completion=$child.WaitForExitAsync();$maximumElapsed=[TimeSpan]::Zero
  while(-not $completion.IsCompleted){
   $utcElapsed=[DateTimeOffset]::UtcNow-$outerStartedUtc
   if($outerClock.Elapsed -gt $maximumElapsed){$maximumElapsed=$outerClock.Elapsed}
   if($utcElapsed -gt $maximumElapsed){$maximumElapsed=$utcElapsed}
   $left=$remaining-$maximumElapsed
   if($left -le [TimeSpan]::Zero){throw 'Outer soak deadline reached; setup/build/test tree terminated. Inspect partial summary; do not claim a full run.'}
   # UTC corrections/suspension have no change notification. Reconcile at most
   # once per second, awaiting process exit or deadline; never busy-poll.
   $pause=if($left -lt [TimeSpan]::FromSeconds(1)){$left}else{[TimeSpan]::FromSeconds(1)}
   $wake=[Threading.CancellationTokenSource]::new();$delay=[Threading.Tasks.Task]::Delay($pause,$wake.Token)
   try{[Threading.Tasks.Task]::WhenAny([Threading.Tasks.Task[]]@($completion,$delay)).GetAwaiter().GetResult()|Out-Null}
   finally{$wake.Cancel();try{$delay.GetAwaiter().GetResult()}catch{if(-not $wake.IsCancellationRequested){throw}};$wake.Dispose()}
  }
  $completion.GetAwaiter().GetResult()
  $exit=$child.ExitCode
 }finally{if(-not $child.HasExited){$child.Kill($true)};$child.WaitForExit();$child.Dispose()}
 $state.state=if($exit -eq 0){'worker_returned'}else{'worker_failed'}
 $state['exit_code']=$exit;$state['returned_utc']=[DateTime]::UtcNow.ToString('o')
 $state|ConvertTo-Json -Depth 5|Set-Content -LiteralPath $pointer
 $state|ConvertTo-Json -Depth 5|Set-Content -LiteralPath (Join-Path $ArtifactDirectory 'launch-manifest.json')
 if($exit){throw "Endurance worker failed: $exit; inspect artifact logs and summary"}
}catch{
 if($state){$state.state='launcher_failed';$state['error']=$_.Exception.Message;$state|ConvertTo-Json -Depth 5|Set-Content -LiteralPath $pointer;$state|ConvertTo-Json -Depth 5|Set-Content -LiteralPath (Join-Path $ArtifactDirectory 'launch-manifest.json')}
 throw
}finally{$guard.Dispose()}
