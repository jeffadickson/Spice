#requires -Version 7.0
[CmdletBinding()]
param(
 [Parameter(Mandatory)][string]$Checkout,
 [Parameter(Mandatory)][string]$Corpus,
 [Parameter(Mandatory)][string]$Runner,
 [Parameter(Mandatory)][string]$Ngspice,
 [Parameter(Mandatory)][string]$Inventory,
 [Parameter(Mandatory)][string]$OutputDirectory
)
$ErrorActionPreference='Stop'
foreach($p in @($Checkout,$Corpus,(Split-Path $OutputDirectory -Parent))){if(!(Test-Path -LiteralPath $p -PathType Container)){throw "Missing directory: $p"}}
foreach($p in @($Runner,$Ngspice,$Inventory)){if(!(Test-Path -LiteralPath $p -PathType Leaf)){throw "Missing file: $p"}}
if(Test-Path -LiteralPath $OutputDirectory){throw 'Fresh reference output required'}
if($IsWindows -and [IO.Path]::GetPathRoot([IO.Path]::GetFullPath($OutputDirectory)) -ne 'E:\'){throw 'Keep reference outputs on E:'}
New-Item -ItemType Directory -Path $OutputDirectory|Out-Null
$live=Join-Path $OutputDirectory 'live-root';New-Item -ItemType Directory -Path $live|Out-Null
$tests=Join-Path $live 'tests';New-Item -ItemType Directory -Path $tests|Out-Null
$saved=@{};foreach($n in @('TEMP','TMP','RSPICE_NGSPICE_LIVE_REFERENCES','NGSPICE_SOURCE_ROOT','NGSPICE_EXE','RSPICE_NGSPICE_REFERENCE_TIMEOUT_MS','RSPICE_GRID_LOCKED')){$saved[$n]=[Environment]::GetEnvironmentVariable($n,'Process')}
$before=Get-Location;$rows=@();$runnerHash=(Get-FileHash -LiteralPath $Runner).Hash.ToLowerInvariant()
try {
 $env:TEMP=$OutputDirectory;$env:TMP=$OutputDirectory;$env:RSPICE_NGSPICE_LIVE_REFERENCES='1';$env:NGSPICE_SOURCE_ROOT=$live;$env:NGSPICE_EXE=[IO.Path]::GetFullPath($Ngspice);$env:RSPICE_NGSPICE_REFERENCE_TIMEOUT_MS='120000';$env:RSPICE_GRID_LOCKED=$null
 if(!('IntelProcessProbe' -as [type])){Add-Type -Path (Join-Path $Checkout 'evaluation/IntelGpuProbe/ProcessProbe.cs')}
 $items=(Get-Content -LiteralPath $Inventory -Raw|ConvertFrom-Json).rows
 if($items.Count -lt 1 -or $items.Count -gt 128){throw 'Reference cohort size must be 1..128'}
 Set-Location -LiteralPath $OutputDirectory
 foreach($item in $items){
  $base=if($item.scope -eq 'repository'){$Checkout}elseif($item.scope -eq 'ngspice'){$Corpus}else{throw 'Unknown inventory scope'}
  $base=[IO.Path]::GetFullPath($base);$original=[IO.Path]::GetFullPath($item.path,$base)
  if(!$original.StartsWith([IO.Path]::TrimEndingDirectorySeparator($base)+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){throw 'Inventory path escapes its declared root'}
  $hash=(Get-FileHash -LiteralPath $original).Hash.ToLowerInvariant();if($hash -ne $item.result.Sha256){throw 'Original input hash changed'}
  $relative=if($item.scope -eq 'repository'){'repository/'+$item.path}elseif($item.path.StartsWith('tests/')){$item.path.Substring(6)}else{$item.path}
  $parts=$relative -split '[\\/]';$directory=$tests
  foreach($part in $parts[0..($parts.Length-2)]){if(!(Test-Path -LiteralPath $directory -PathType Container)){throw 'Missing source parent'};$directory=Join-Path $directory $part;if(!(Test-Path -LiteralPath $directory -PathType Container)){New-Item -ItemType Directory -Path $directory|Out-Null}}
  $copy=Join-Path $directory $parts[-1];Copy-Item -LiteralPath $original -Destination $copy
  if((Get-FileHash -LiteralPath $copy).Hash.ToLowerInvariant() -ne $hash){throw 'Reference copy differs from original'}
  $testRoot=Join-Path $tests $parts[0]
  if($relative.StartsWith('ngspice/')){
   $manifest=Join-Path $testRoot 'validation-manifest.tsv';$sourceManifest=Join-Path $Corpus 'tests/ngspice/validation-manifest.tsv'
   if(!(Test-Path -LiteralPath $manifest) -and (Test-Path -LiteralPath $sourceManifest -PathType Leaf)){Copy-Item -LiteralPath $sourceManifest -Destination $manifest}
  }
  $ordinal=$rows.Count;$resultFile=Join-Path $OutputDirectory "rspice-$ordinal.result"
  $arguments=[string[]]@('--test-dir',$testRoot,'--circuit',$copy,'--result',$resultFile,'--relative-tolerance','0.0001','--absolute-tolerance','0.000000001','--skip-unsupported','false','--max-time-per-test-ms','120000','--verbose','true')
  $process=[IntelProcessProbe]::RunMany($Runner,$arguments,$null,150000)
  $process|ConvertTo-Json -Depth 5|Set-Content -LiteralPath (Join-Path $OutputDirectory "rspice-$ordinal.process.json")
  $state='unavailable';$analysis=$null;$errorText=$null;$mismatches=$null;$upstreamPass=$false
  if($process.exited -and $process.exit_code -eq 0 -and !$process.clipped -and (Test-Path -LiteralPath $resultFile -PathType Leaf)){
   $lines=Get-Content -LiteralPath $resultFile;if($lines[0] -ne 'RSPICE_TEST_RESULT_V1'){throw 'Unknown RSpice result protocol'}
   $fields=@{};foreach($line in $lines|Select-Object -Skip 1){$columns=$line -split "`t";if($columns[0] -in @('passed','analysis_type','error','mismatches')){if($fields.ContainsKey($columns[0])){throw 'Duplicate RSpice result field'};$fields[$columns[0]]=$columns}}
  $upstreamPass=$fields['passed'][1] -eq '1';$mismatches=[int]$fields['mismatches'][1]
   if($fields['analysis_type'][1] -eq '1'){$analysis=[Uri]::UnescapeDataString($fields['analysis_type'][2])}
   if($fields['error'][1] -eq '1'){$errorText=[Uri]::UnescapeDataString($fields['error'][2])}
   $state=if(!$upstreamPass -and $errorText -match 'Reference comparison error|reference setup error|no comparable table'){'unavailable_reference_comparison'}elseif(!$upstreamPass){'failed'}elseif($analysis -match 'SKIP|EXPECTED|SMOKE'){'explicit_skip_or_non_numerical_contract'}elseif($errorText){'passed_with_contract_notice'}else{'passed_upstream_comparison'}
  }
  $unchanged=(Get-FileHash -LiteralPath $original).Hash.ToLowerInvariant() -eq $hash -and (Get-FileHash -LiteralPath $copy).Hash.ToLowerInvariant() -eq $hash
  $rows+=@{scope=$item.scope;path=$item.path;input_sha256=$hash;source_unchanged=$unchanged;state=$state;upstream_pass=$upstreamPass;analysis=$analysis;error=$errorText;mismatches=$mismatches;process_elapsed_seconds=$process.elapsed_seconds;reference='fresh ngspice, original numerical controls; no grid locking';bounds=@{absolute=1e-9;relative=1e-4}}
 }
 $unchanged=(Get-FileHash -LiteralPath $Runner).Hash.ToLowerInvariant() -eq $runnerHash
 [ordered]@{runner_sha256=$runnerHash;runner_unchanged=$unchanged;ngspice_sha256=(Get-FileHash -LiteralPath $Ngspice).Hash.ToLowerInvariant();rows=$rows;limits=@('Reused pinned-reference executable, not a fresh clean-build attestation.','Upstream comparator semantics and signal/analysis coverage remain distinct from our full mapped-observer comparator.','Elapsed includes fresh ngspice reference generation/comparison, so it is not a RSpice solver speed benchmark.','Skip, expected-unsupported and smoke results are never numerical agreement.')}|ConvertTo-Json -Depth 10|Set-Content -LiteralPath (Join-Path $OutputDirectory 'summary.json')
 if(!$unchanged -or @($rows|Where-Object {!$_.source_unchanged}).Count){throw 'Reference provenance changed'}
} finally {Set-Location -LiteralPath $before.Path;foreach($n in $saved.Keys){if($null -eq $saved[$n]){Remove-Item -LiteralPath ("Env:"+$n) -ErrorAction SilentlyContinue}else{[Environment]::SetEnvironmentVariable($n,$saved[$n],'Process')}}}
