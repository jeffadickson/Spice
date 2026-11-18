param([Parameter(Mandatory)][string]$Configuration,[switch]$ValidateOnly)
# Orchestrate the maintained cargo-mutants framework; native reports are the
# durable authority. Checkpoints are derived views, never invented test outcomes.
$ErrorActionPreference='Stop'
if(!(Test-Path -LiteralPath $Configuration -PathType Leaf)){throw 'Missing backlog configuration'}
$config=Get-Content -LiteralPath $Configuration -Raw|ConvertFrom-Json
foreach($path in @($config.source,$config.artifacts,$config.framework_work_root,$config.canonical_repository)){
 if(!(Test-Path -LiteralPath $path -PathType Container)){throw "Missing directory: $path"}
}
foreach($path in @($config.pending_file,$config.framework_helper)){
 if(!(Test-Path -LiteralPath $path -PathType Leaf)){throw "Missing file: $path"}
}
if((Get-FileHash -LiteralPath $config.pending_file).Hash.ToLowerInvariant() -ne $config.pending_sha256){throw 'Pending selection hash mismatch'}
if((Get-FileHash -LiteralPath $config.framework_helper).Hash.ToLowerInvariant() -ne $config.helper_sha256){throw 'Framework helper hash mismatch'}
$names=@(Get-Content -LiteralPath $config.pending_file -Raw|ConvertFrom-Json)
if(!$names.Count -or @($names|Sort-Object -Unique).Count -ne $names.Count){throw 'Empty or duplicate backlog'}
foreach($name in $names){if($name -notmatch '^src/[a-zA-Z0-9_]+\.rs:[1-9][0-9]*:[1-9][0-9]*: [^\r\n]+$' -or $name.Length -gt 512){throw 'Unsafe exact mutant name'}}
function Write-DispatchManifest {
 param([string]$SelectionFile,[string]$Run='')
 # Publish the small identity binding by replacement, so interruption cannot
 # expose a half-written JSON document to the next invocation.
 $target=$SelectionFile+'.manifest.json';$temporary=$target+'.tmp'
 @{campaign_identity=$campaignIdentity;selection_sha256=(Get-FileHash -LiteralPath $SelectionFile).Hash.ToLowerInvariant();run=$Run}|ConvertTo-Json|Set-Content -LiteralPath $temporary
 Move-Item -LiteralPath $temporary -Destination $target -Force
}
function Confirm-Inputs {
 foreach($item in $config.input_hashes.PSObject.Properties){
  $path=Join-Path $config.source $item.Name
  if(!(Test-Path -LiteralPath $path -PathType Leaf) -or (Get-FileHash -LiteralPath $path).Hash.ToLowerInvariant() -ne $item.Value){throw "Source input changed: $($item.Name)"}
 }
}
Confirm-Inputs
# Bind evidence to all numerical/test inputs and tool identities, independent of
# an invocation's shorter time window. Selection names alone are insufficient.
$identityText=@($config.source_commit,$config.source,$config.pending_sha256,$config.helper_sha256,$config.seed,'mutants/lib,mutation_suite',($config.qualification_overlay -join '|'))
$identityText+=@($config.input_hashes.PSObject.Properties|Sort-Object Name|ForEach-Object {$_.Name+'='+$_.Value})
$campaignIdentity=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes(($identityText -join "`n")))).ToLowerInvariant()
if($ValidateOnly){[ordered]@{status='validated';campaign_identity=$campaignIdentity;candidates=$names.Count;source=$config.source;source_commit=$config.source_commit}|ConvertTo-Json;return}
$env:TEMP=Join-Path $config.artifacts 'temp';$env:TMP=$env:TEMP
if(!(Test-Path -LiteralPath $env:TEMP -PathType Container)){throw 'Missing E: temporary directory'}
if($IsWindows -and [IO.Path]::GetPathRoot([IO.Path]::GetFullPath($env:TEMP)) -ne 'E:\'){throw 'Windows scratch must be on E:'}
$env:CODEX_ARTIFACT_ROOT=$config.artifacts;$env:SPICE_FRAMEWORK_WORK_ROOT=$config.framework_work_root
$env:SPICE_CARGO_MUTANTS=$config.cargo_mutants;$env:NUGET_PACKAGES=$config.nuget_packages
$env:CARGO_PROFILE_MUTANTS_INCREMENTAL='false';$env:CARGO_PROFILE_MUTANTS_CODEGEN_UNITS='16'
$env:PROPTEST_RNG_SEED=$config.seed
$lockPath=Join-Path $config.canonical_repository 'work/endurance.lock'
# Preserve DateTime.Kind when JSON has already decoded an ISO UTC timestamp.
$guard=[IO.File]::Open($lockPath,'OpenOrCreate','ReadWrite','None')
$started=[DateTimeOffset]::UtcNow;$deadline=$started.AddMinutes($config.minutes)
if($config.finish_by_utc){$finish=([DateTimeOffset]$config.finish_by_utc);if($finish -lt $deadline){$deadline=$finish}}
if($deadline -le $started){$guard.Dispose();throw 'Backlog deadline already expired'}
$campaignStarted=$started
$statePath=Join-Path $config.artifacts 'summary.json'
$pointer=Join-Path $config.canonical_repository 'work/verified-soak-active.json'
$done=@{};$runs=[Collections.Generic.List[string]]::new();$completed=$false;$child=$null
function Save-Backlog {
 param([string]$Status,[string]$ErrorText='')
 $rows=@($done.Values|Sort-Object name)
 $state=[ordered]@{kind='mutation_backlog';campaign_identity=$campaignIdentity;status=$Status;source_commit=$config.source_commit;
  source_dirty=$config.source_dirty;product_source_changed=$false;source_checkout=$config.source;
  campaign_started_utc=$campaignStarted.ToString('o');invocation_started_utc=$started.ToString('o');
  qualification_overlay=$config.qualification_overlay;pending_sha256=$config.pending_sha256;started_utc=$started.ToString('o');
  finished_utc=$(if($Status -in @('completed_needs_findings_review','failed','budget_exhausted')){[DateTimeOffset]::UtcNow.ToString('o')}else{$null});
  deadline_utc=$deadline.ToString('o');elapsed_seconds=([DateTimeOffset]::UtcNow-$started).TotalSeconds;
  requested_candidates=$names.Count;completed_candidates=$rows.Count;unattempted=$names.Count-$rows.Count;
  outcome_counts=@($rows|Group-Object outcome|Select-Object Name,Count);outcomes=$rows;framework_runs=$runs.ToArray();
  worker_pid=$PID;worker_started_utc=(Get-Process -Id $PID).StartTime.ToUniversalTime().ToString('o');
  artifact_directory=$config.artifacts;summary_path=$statePath;canonical_lock=$lockPath;error=$ErrorText}
 $temporary=$statePath+'.tmp';[IO.File]::WriteAllText($temporary,($state|ConvertTo-Json -Depth 9))
 for($attempt=0;;$attempt++){try{Move-Item -LiteralPath $temporary -Destination $statePath -Force;break}catch [IO.IOException]{if($attempt -ge 20){throw};[Threading.Thread]::Sleep(100)}}
 # A bounded pointer lets the scheduler preserve the actual lock-owning worker.
 $ptr=[ordered]@{kind='mutation_backlog';state=$Status;source_commit=$config.source_commit;source_dirty=$config.source_dirty;
  product_source_changed=$false;worker_pid=$PID;worker_started_utc=$state.worker_started_utc;started_utc=$state.started_utc;
  artifact_directory=$config.artifacts;summary_path=$statePath;canonical_lock=$lockPath;deadline_utc=$state.deadline_utc}
 $pointerTemporary=$pointer+'.tmp';[IO.File]::WriteAllText($pointerTemporary,($ptr|ConvertTo-Json -Depth 4))
 Move-Item -LiteralPath $pointerTemporary -Destination $pointer -Force
}
function Import-NativeProgress {
 param([string]$Run)
 $resolved=[IO.Path]::GetFullPath($Run)
 $allowed=[IO.Path]::GetFullPath($config.framework_work_root).TrimEnd([IO.Path]::DirectorySeparatorChar)+[IO.Path]::DirectorySeparatorChar
 if(!$resolved.StartsWith($allowed,[StringComparison]::OrdinalIgnoreCase)){throw 'Native report outside framework root'}
 $path=Join-Path $Run 'rust/mutants.out/outcomes.json'
 if(!(Test-Path -LiteralPath $path -PathType Leaf)){return}
 try{$native=Get-Content -LiteralPath $path -Raw|ConvertFrom-Json}catch{ return } # Writer may be replacing the native checkpoint.
 foreach($outcome in $native.outcomes){
  if($outcome.scenario -is [string]){continue};$name=$outcome.scenario.Mutant.name
  if($name -notin $names){continue} # Supplementary tool-field candidates are separate evidence.
  if($outcome.summary -notin @('CaughtMutant','MissedMutant','Unviable','Timeout')){continue}
  if(!$done.ContainsKey($name)){$done[$name]=[pscustomobject][ordered]@{name=$name;outcome=$outcome.summary;run=$Run;log=$outcome.log_path;diff=$outcome.diff_path}}
 }
}
try{
 # Child processes receive an explicit source working directory; preserve caller location.
 # Recover from native reports even after a crash between an outcome and Save.
 if(Test-Path -LiteralPath $statePath -PathType Leaf){
  $old=Get-Content -LiteralPath $statePath -Raw|ConvertFrom-Json
  if($old.campaign_identity -ne $campaignIdentity){
   $legacyValid=$false
   if(!$old.campaign_identity -and $config.legacy_configuration_path -and (Test-Path -LiteralPath $config.legacy_configuration_path -PathType Leaf)){
    if((Get-FileHash -LiteralPath $config.legacy_configuration_path).Hash.ToLowerInvariant() -eq $config.legacy_configuration_sha256){
     $legacy=Get-Content -LiteralPath $config.legacy_configuration_path -Raw|ConvertFrom-Json
     $legacyInputs=@($legacy.source_commit,$legacy.source,$legacy.pending_sha256,$legacy.helper_sha256,$legacy.seed,'mutants/lib,mutation_suite',($legacy.qualification_overlay -join '|'))
     $legacyInputs+=@($legacy.input_hashes.PSObject.Properties|Sort-Object Name|ForEach-Object {$_.Name+'='+$_.Value})
     $legacyValid=($legacyInputs -join "`n") -ceq ($identityText -join "`n")
    }
   }
   if(!$legacyValid){throw 'Checkpoint campaign provenance mismatch; explicit legacy import required'}
  }
  if($old.source_commit -ne $config.source_commit -or $old.pending_sha256 -ne $config.pending_sha256 -or $old.requested_candidates -ne $names.Count){throw 'Checkpoint identity mismatch'}
  if($old.campaign_started_utc){$campaignStarted=([DateTimeOffset]$old.campaign_started_utc)}elseif($old.started_utc){$campaignStarted=([DateTimeOffset]$old.started_utc)}
  foreach($run in $old.framework_runs){$runs.Add($run);Import-NativeProgress $run}
 }
 # Reconcile dispatches whose native outcomes survived a controller crash before
 # the next checkpoint. Matching selection hashes prevent importing other jobs.
 $selectionHashes=@(Get-ChildItem -LiteralPath $config.artifacts -Filter 'selection-*.json' -File|Where-Object {$manifest=$_.FullName+'.manifest.json'; if(!(Test-Path -LiteralPath $manifest -PathType Leaf)){return $false}; (Get-Content -LiteralPath $manifest -Raw|ConvertFrom-Json).campaign_identity -eq $campaignIdentity}|ForEach-Object{(Get-FileHash -LiteralPath $_.FullName).Hash.ToLowerInvariant()})
 foreach($directory in Get-ChildItem -LiteralPath $config.framework_work_root -Directory|Where-Object Name -match '^2026'){
  $meta=Join-Path $directory.FullName 'summary.json';if(!(Test-Path -LiteralPath $meta -PathType Leaf)){continue}
  try{$report=Get-Content -LiteralPath $meta -Raw|ConvertFrom-Json}catch{continue}
  $binding=@(Get-ChildItem -LiteralPath $config.artifacts -Filter 'selection-*.json.manifest.json' -File|ForEach-Object {Get-Content -LiteralPath $_.FullName -Raw|ConvertFrom-Json}|Where-Object {$_.campaign_identity -eq $campaignIdentity -and $_.run -eq $directory.FullName -and $_.selection_sha256 -eq $report.rust_selection_sha256})
  if($report.rust_selection_sha256 -in $selectionHashes -and $binding.Count -eq 1){if($directory.FullName -notin $runs){$runs.Add($directory.FullName)};Import-NativeProgress $directory.FullName}
 }
 Save-Backlog 'running'
 while($done.Count -lt $names.Count -and [DateTimeOffset]::UtcNow -lt $deadline){
  Confirm-Inputs
  $pending=@($names|Where-Object {!$done.ContainsKey($_)})
  $selection=[Collections.Generic.List[string]]::new();$size=0
  foreach($name in $pending){$cost=[regex]::Escape($name).Length+1;if($selection.Count -ge 32 -or $size+$cost -gt 7000){break};$selection.Add($name);$size+=$cost}
  if(!$selection.Count){throw 'No safe exact-name batch fits'}
  $files=@($selection|ForEach-Object {($_ -split ':',2)[0]}|Sort-Object -Unique)
  if('src/netlist.rs' -in $files){foreach($extra in $config.supplementary_names){if($extra -notin $selection){$selection.Add($extra)}}}
  $glob=if($files.Count -eq 1){$files[0]}else{'src/{'+(($files|ForEach-Object {[IO.Path]::GetFileNameWithoutExtension($_)}) -join ',')+'}.rs'}
  $stamp=[DateTime]::UtcNow.ToString('yyyyMMddTHHmmssZ')+'-'+[guid]::NewGuid().ToString('N').Substring(0,8)
  $selectionFile=Join-Path $config.artifacts ('selection-'+$stamp+'.json')
  ConvertTo-Json -InputObject $selection.ToArray()|Set-Content -LiteralPath $selectionFile
  Write-DispatchManifest $selectionFile
  $output=Join-Path $config.artifacts ('batch-'+$stamp+'.log')
  $minutes=[Math]::Min(90,($deadline-[DateTimeOffset]::UtcNow).Add([TimeSpan]::FromSeconds(-5)).TotalMinutes)
  if($minutes -le 0){break}
  $start=[Diagnostics.ProcessStartInfo]::new((Get-Command dotnet).Source);$start.UseShellExecute=$false;$start.CreateNoWindow=$true;$start.WorkingDirectory=$config.source
  foreach($arg in @($config.framework_helper,'--tool','rust','--minutes',$minutes.ToString([cultureinfo]::InvariantCulture),'--workers','2','--test-workers','2','--rust-profile','mutants','--rust-test','lib,mutation_suite','--rust-file',$glob,'--rust-names-file',$selectionFile)){$start.ArgumentList.Add($arg)}
  $start.RedirectStandardOutput=$true;$start.RedirectStandardError=$true
  $child=[Diagnostics.Process]::Start($start);if(!$child){throw 'Framework did not start'}
  $stdout=$child.StandardOutput.ReadToEndAsync();$stderr=$child.StandardError.ReadToEndAsync()
  $before=$done.Count;$nativeRun=$null
  while(!$child.WaitForExit(15000)){
   # cargo-mutants publishes durable outcomes; bounded reconciliation survives
   # coalesced/missed filesystem events and process exit races.
   $candidates=@(Get-ChildItem -LiteralPath $config.framework_work_root -Directory|Where-Object {$_.Name -match '^2026' -and $_.CreationTimeUtc -ge $started.UtcDateTime}|Sort-Object Name)
   $selectionHash=(Get-FileHash -LiteralPath $selectionFile).Hash.ToLowerInvariant()
   foreach($candidate in $candidates){
    $meta=Join-Path $candidate.FullName 'summary.json';if(!(Test-Path -LiteralPath $meta -PathType Leaf)){continue}
    try{$report=Get-Content -LiteralPath $meta -Raw|ConvertFrom-Json}catch{continue}
    if($report.rust_selection_sha256 -ne $selectionHash){continue}
    $nativeRun=$candidate.FullName;Write-DispatchManifest $selectionFile $nativeRun;if($nativeRun -notin $runs){$runs.Add($nativeRun)};Import-NativeProgress $nativeRun;Save-Backlog 'running'
   }
   if([DateTimeOffset]::UtcNow -ge $deadline){$child.Kill($true);$child.WaitForExit();break}
  }
  $text=$stdout.GetAwaiter().GetResult()+"`n"+$stderr.GetAwaiter().GetResult();[IO.File]::WriteAllText($output,$text)
  $marker=[regex]::Match($text,'(?m)^Framework summary: (.+)\r?$')
  if(!$marker.Success){throw 'Framework emitted no report identity'}
  $nativeRun=Split-Path -Parent $marker.Groups[1].Value.Trim();Write-DispatchManifest $selectionFile $nativeRun;if($nativeRun -notin $runs){$runs.Add($nativeRun)}
  Import-NativeProgress $nativeRun;Save-Backlog 'running'
  $exit=$child.ExitCode;$child.Dispose();$child=$null
  if($exit -ne 0){throw "Framework failed: $exit; see $output"}
  if($done.Count -eq $before){throw "Framework produced no new cohort outcome; see $output"}
 }
 $completed=$done.Count -eq $names.Count
 Save-Backlog $(if($completed){'completed_needs_findings_review'}else{'budget_exhausted'})
}catch{Save-Backlog 'failed' $_.Exception.Message;throw}
finally{if($child){if(!$child.HasExited){$child.Kill($true);$child.WaitForExit()};$child.Dispose()};$guard.Dispose()}
