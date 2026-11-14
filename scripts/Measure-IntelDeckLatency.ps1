#requires -Version 7.0
[CmdletBinding()]
param(
 [Parameter(Mandatory)][string]$Engine,
 [Parameter(Mandatory)][string]$Provider,
 [Parameter(Mandatory)][string[]]$Requests,
 [Parameter(Mandatory)][string]$OutputDirectory,
 [ValidateRange(3,10)][int]$Repetitions=3,
 [ValidateRange(1000,600000)][int]$TimeoutMilliseconds=120000
)
# Compare the same public protocol and executable. These are end-to-end one-shot
# process latencies, including assembly, transfer, solve, audits and shutdown;
# they are not GPU kernel timings or isolated factorization benchmarks.
$ErrorActionPreference='Stop'
foreach($p in @($Engine,$Provider)+$Requests){if(!(Test-Path -LiteralPath $p -PathType Leaf)){throw "Missing input: $p"}}
if(!(Test-Path -LiteralPath (Split-Path $OutputDirectory -Parent) -PathType Container)){throw 'Missing output parent'}
if(Test-Path -LiteralPath $OutputDirectory){throw 'Fresh output required'}
if($IsWindows -and [IO.Path]::GetPathRoot([IO.Path]::GetFullPath($OutputDirectory)) -ne 'E:\'){throw 'Bulk outputs must be on E:'}
New-Item -ItemType Directory -Path $OutputDirectory|Out-Null
$saved=@{};foreach($n in @('TEMP','TMP','SPICE_INTEL_HOST','VIENNACL_CACHE_PATH','NEO_CACHE_DIR')){$saved[$n]=[Environment]::GetEnvironmentVariable($n,'Process')}
try {
 $env:TEMP=$OutputDirectory;$env:TMP=$OutputDirectory;$env:SPICE_INTEL_HOST=[IO.Path]::GetFullPath($Provider)
 $cache=Join-Path $OutputDirectory 'opencl-cache';New-Item -ItemType Directory -Path $cache|Out-Null
 $env:VIENNACL_CACHE_PATH=$cache+'/';$env:NEO_CACHE_DIR=$cache
 Add-Type -Path (Join-Path (Split-Path $PSScriptRoot -Parent) 'evaluation/IntelGpuProbe/ProcessProbe.cs')
 $engineHash=(Get-FileHash -LiteralPath $Engine).Hash;$providerHash=(Get-FileHash -LiteralPath $Provider).Hash
 $rows=@();$index=0
 foreach($path in $Requests){
  $request=Get-Content -LiteralPath $path -Raw|ConvertFrom-Json -AsHashtable
  foreach($backend in @('lu','faer','intel')){
   $request.backend=$backend;$inputJson=$request|ConvertTo-Json -Depth 100 -Compress
   for($iteration=0;$iteration -le $Repetitions;$iteration++){
    $r=[IntelProcessProbe]::Run($Engine,'',$inputJson,$TimeoutMilliseconds)
    $stem="case-$index-$backend-$iteration";$r|ConvertTo-Json -Depth 8|Set-Content -LiteralPath (Join-Path $OutputDirectory ($stem+'.json'))
    $identity=$null;$success=$false
    if($r.exited -and $r.exit_code -eq 0 -and !$r.clipped){
     $response=$r.stdout|ConvertFrom-Json;$identity=$response.backend.id
     # Metadata is part of acceptance: a CPU fallback is never an Intel sample.
     $expected=@{lu='nalgebra_dense_lu_reference';faer='faer_sparse_lu';intel='intel_opencl_viennacl_gmres_experimental'}[$backend]
     $success=$response.complete -and $response.status -eq 'succeeded' -and $identity -eq $expected -and ($backend -ne 'intel' -or ($response.backend.gpu -and $response.backend.placement -eq 'gpu'))
    }
    $rows+=@{case=$index;request=$path;request_sha256=(Get-FileHash -LiteralPath $path).Hash;backend=$backend;iteration=$iteration;warmup=($iteration -eq 0);seconds=$r.elapsed_seconds;success=$success;identity=$identity;artifact=($stem+'.json')}
    # Repeating a failed warmup is not a meaningful latency distribution. Keep
    # its failure and mark measured samples absent instead of spending another
    # three full deadlines or treating a timeout as a successful timing sample.
    if($iteration -eq 0 -and !$success){break}
   }
  };$index++
 }
 $valid=$engineHash -eq (Get-FileHash -LiteralPath $Engine).Hash -and $providerHash -eq (Get-FileHash -LiteralPath $Provider).Hash
 @{rows=$rows;runtime_provenance_valid=$valid;engine_sha256=$engineHash;provider_sha256=$providerHash;timeout_milliseconds=$TimeoutMilliseconds;scope='Same executable and circuit JSON except backend; first iteration excluded; fresh child per sample; private shader cache shared within run; dev Rust and Release native adapter, no release-speed claim'}|ConvertTo-Json -Depth 10|Set-Content -LiteralPath (Join-Path $OutputDirectory 'summary.json')
 if(!$valid){throw 'Runtime changed during measurements'}
} finally {foreach($n in $saved.Keys){if($null -eq $saved[$n]){Remove-Item -LiteralPath ('Env:'+$n) -ErrorAction SilentlyContinue}else{[Environment]::SetEnvironmentVariable($n,$saved[$n],'Process')}}}
