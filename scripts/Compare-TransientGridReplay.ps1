#requires -Version 7.0
[CmdletBinding()]
param(
 [Parameter(Mandatory)][string]$ManagedBin,
 [Parameter(Mandatory)][string]$Engine,
 [Parameter(Mandatory)][string]$BaselineDirectory,
 [Parameter(Mandatory)][string]$OutputDirectory
)
$ErrorActionPreference='Stop'
foreach($p in @($ManagedBin,$BaselineDirectory,(Split-Path $OutputDirectory -Parent))){if(!(Test-Path -LiteralPath $p -PathType Container)){throw "Missing directory: $p"}}
if(!(Test-Path -LiteralPath $Engine -PathType Leaf)){throw 'Missing engine'}
if(Test-Path -LiteralPath $OutputDirectory){throw 'Fresh output required'}
if($IsWindows -and [IO.Path]::GetPathRoot([IO.Path]::GetFullPath($OutputDirectory)) -ne 'E:\'){throw 'Keep artifacts on E:'}
$baseline=Get-Content -LiteralPath (Join-Path $BaselineDirectory 'summary.json') -Raw|ConvertFrom-Json
if(!$baseline.provenance_valid -or $baseline.state -ne 'completed_with_results'){throw 'Baseline must have a completed immutable-source gate'}
$engineBefore=(Get-FileHash -LiteralPath $Engine).Hash
$Engine=[IO.Path]::GetFullPath($Engine)
$engineEntry=@($baseline.source_hashes.PSObject.Properties|Where-Object {$_.Name -eq $Engine})
if($engineEntry.Count -ne 1 -or $engineEntry[0].Value -ne $engineBefore.ToLowerInvariant()){throw 'Baseline engine differs or lacks provenance'}
$hashes=[ordered]@{};$hashes[$Engine]=$engineBefore
$baselinePath=Join-Path $BaselineDirectory 'summary.json';$hashes[$baselinePath]=(Get-FileHash -LiteralPath $baselinePath).Hash
foreach($assembly in @('Antlr4.Runtime.Standard','Simulator.Client','Simulator.Syntax','Simulator.Frontend','Simulator.RequestedQualification')){
 $path=Join-Path $ManagedBin ($assembly+'.dll');if(!(Test-Path -LiteralPath $path -PathType Leaf)){throw "Missing assembly: $path"};$hashes[$path]=(Get-FileHash -LiteralPath $path).Hash
 if($assembly -ne 'Antlr4.Runtime.Standard'){
  $recorded=@($baseline.source_hashes.PSObject.Properties|Where-Object {[IO.Path]::GetFileName($_.Name) -eq ($assembly+'.dll')})
  if($recorded.Count -ne 1 -or $recorded[0].Value -ne $hashes[$path]){throw 'Managed assembly differs from baseline'}
 }
 $loaded=[Reflection.Assembly]::LoadFrom($path);$hashes[$loaded.Location]=(Get-FileHash -LiteralPath $loaded.Location).Hash
 if($hashes[$loaded.Location] -ne $hashes[$path]){throw 'A different managed assembly is already loaded'}
}
New-Item -ItemType Directory -Path $OutputDirectory|Out-Null
$json=[Text.Json.JsonSerializerOptions]::new();$json.WriteIndented=$true;$json.PropertyNamingPolicy=[Text.Json.JsonNamingPolicy]::SnakeCaseLower
$client=[Simulator.Client.SimulationClient]::new($Engine);$results=@()
foreach($row in $baseline.rows){
 if(!$row.circuit_prepared){throw 'Unprepared baseline row'}
 $hashes[$row.input_path]=(Get-FileHash -LiteralPath $row.input_path).Hash
 if($hashes[$row.input_path].ToLowerInvariant() -ne $row.input_sha256){throw 'Baseline input changed'}
 $syntax=[Simulator.Syntax.DeckParser]::Parse([IO.File]::ReadAllText($row.input_path),$null,$false,$true)
 $plan=[Simulator.Frontend.RequestedAnalysis]::Prepare($syntax,10000,$null,$baseline.backend)
 if(!$plan.Success){throw 'Replay plan refused'}
 for($ordinal=0;$ordinal -lt $plan.Operations.Length;$ordinal++){$operation=$plan.Operations[$ordinal];if($operation.Kind -ne 'tran'){continue}
  $rowIndex=[array]::IndexOf($baseline.rows,$row);$folder=Join-Path $BaselineDirectory ("cases/{0:D4}/{1}" -f $rowIndex,('requested_'+$ordinal))
  $rawPath=Join-Path $folder 'native.raw';if(!(Test-Path -LiteralPath $rawPath -PathType Leaf)){throw 'Complete native raw baseline required'}
  $hashes[$rawPath]=(Get-FileHash -LiteralPath $rawPath).Hash
  if($hashes[$rawPath].ToLowerInvariant() -ne $row.operations[$ordinal].native_raw_sha256){throw 'Native reference changed'}
  $native=[Simulator.RequestedQualification.AsciiRaw]::Read([IO.File]::ReadAllText($rawPath))
  $originalPath=Join-Path $folder '00000.request.json';$hashes[$originalPath]=(Get-FileHash -LiteralPath $originalPath).Hash
  $original=$operation.Requests[0]
  $originalJson=[Text.Json.JsonSerializer]::Serialize($original,$original.GetType(),[Simulator.Client.WireFormat]::Options)
  if(![Text.Json.Nodes.JsonNode]::DeepEquals([Text.Json.Nodes.JsonNode]::Parse([IO.File]::ReadAllText($originalPath)),[Text.Json.Nodes.JsonNode]::Parse($originalJson))){throw 'Regenerated request differs from archived baseline request'}
  $deadline=[Threading.CancellationTokenSource]::new([TimeSpan]::FromSeconds(120))
  try{$result=[Simulator.RequestedQualification.ReferenceGridReplay]::RunAsync($operation,$plan.CircuitPreparation,$native,$client,$deadline.Token).GetAwaiter().GetResult()}finally{$deadline.Dispose()}
  $target=Join-Path $OutputDirectory ("{0:D4}-{1}" -f $rowIndex,$ordinal);New-Item -ItemType Directory -Path $target|Out-Null
  foreach($entry in @(@('original.request.json',$result.Original),@('replay.request.json',$result.Replay))){[IO.File]::WriteAllText((Join-Path $target $entry[0]),[Text.Json.JsonSerializer]::Serialize($entry[1],$entry[1].GetType(),[Simulator.Client.WireFormat]::Options))}
  [IO.File]::WriteAllText((Join-Path $target 'response.json'),$result.Response.Payload.GetRawText())
  [IO.File]::WriteAllText((Join-Path $target 'comparison.json'),[Text.Json.JsonSerializer]::Serialize($result.Comparison,$result.Comparison.GetType(),$json))
  $results+=@{path=$row.path;mode=$result.Mode;backend=$baseline.backend;agreed=$result.Comparison.Agreed;status=$result.Comparison.Status;native_points=$native.Points.Length;input_sha256=$row.input_sha256;native_raw_sha256=(Get-FileHash -LiteralPath $rawPath).Hash;baseline_request_sha256=$hashes[$originalPath];original_request_sha256=(Get-FileHash -LiteralPath (Join-Path $target 'original.request.json')).Hash;replay_request_sha256=(Get-FileHash -LiteralPath (Join-Path $target 'replay.request.json')).Hash;response_sha256=(Get-FileHash -LiteralPath (Join-Path $target 'response.json')).Hash;comparison=$result.Comparison}
 }
}
if(!$results.Count){throw 'No transient replays'}
$valid=$true;foreach($path in $hashes.Keys){if($hashes[$path] -ne (Get-FileHash -LiteralPath $path).Hash){$valid=$false}}
@{mode='reference_grid_physics_replay';runtime_provenance_valid=$valid;input_runtime_sha256=$hashes;engine_sha256=$engineBefore;results=$results;limits=@('Preserves default free-running evidence separately.','Explicit accepted grid disables adaptive control; not an additional stock-default agreeing deck.')}|ConvertTo-Json -Depth 15|Set-Content -LiteralPath (Join-Path $OutputDirectory 'summary.json')
if(!$valid -or @($results|Where-Object {!$_.agreed}).Count){throw 'Reference-grid qualification failed'}
