#requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Checkout,
    [Parameter(Mandatory)][string]$ManagedBin,
    [Parameter(Mandatory)][string]$Engine,
    [Parameter(Mandatory)][string]$Provider,
    [Parameter(Mandatory)][string]$Corpus,
    [Parameter(Mandatory)][string]$Ngspice,
    [Parameter(Mandatory)][string]$Inventory,
    [Parameter(Mandatory)][string]$OutputDirectory
)
$ErrorActionPreference='Stop'
foreach($path in @($Checkout,$ManagedBin,$Corpus,(Split-Path $OutputDirectory -Parent))){if(!(Test-Path -LiteralPath $path -PathType Container)){throw "Missing directory: $path"}}
foreach($path in @($Engine,$Provider,$Ngspice,$Inventory)){if(!(Test-Path -LiteralPath $path -PathType Leaf)){throw "Missing file: $path"}}
if(Test-Path -LiteralPath $OutputDirectory){throw 'Fresh output directory required'}
$OutputDirectory=[IO.Path]::GetFullPath($OutputDirectory)
if($IsWindows -and [IO.Path]::GetPathRoot($OutputDirectory) -ne 'E:\'){throw 'Keep bulk comparison outputs on E:'}
New-Item -ItemType Directory -Path $OutputDirectory | Out-Null
$prior=Get-Location;$saved=@{}
foreach($name in @('TEMP','TMP','SPICE_INTEL_HOST','VIENNACL_CACHE_PATH','NEO_CACHE_DIR','GIT_CONFIG_COUNT','GIT_CONFIG_KEY_0','GIT_CONFIG_VALUE_0')){$saved[$name]=[Environment]::GetEnvironmentVariable($name,'Process')}
try {
    $env:TEMP=$OutputDirectory;$env:TMP=$OutputDirectory;$env:SPICE_INTEL_HOST=[IO.Path]::GetFullPath($Provider)
    # Both the ViennaCL binary cache and Intel's persistent compiler cache have
    # explicit per-run E: destinations. Never redirect unrelated project caches.
    $cache=Join-Path $OutputDirectory 'opencl-cache';New-Item -ItemType Directory -Path $cache | Out-Null
    $env:VIENNACL_CACHE_PATH=$cache+[IO.Path]::DirectorySeparatorChar;$env:NEO_CACHE_DIR=$cache
    # Accept either a repository root or a nested corpus directory. Trust only
    # its discovered repository root, never an assumed grandparent or wildcard.
    $gitRoot=[IO.DirectoryInfo][IO.Path]::GetFullPath($Corpus)
    while($gitRoot -and !(Test-Path -LiteralPath (Join-Path $gitRoot.FullName '.git'))){$gitRoot=$gitRoot.Parent}
    if(!$gitRoot){throw 'Corpus repository metadata unavailable'}
    $env:GIT_CONFIG_COUNT='1';$env:GIT_CONFIG_KEY_0='safe.directory';$env:GIT_CONFIG_VALUE_0=$gitRoot.FullName.Replace('\','/')
    foreach($name in @('Antlr4.Runtime.Standard','Simulator.Client','Simulator.Syntax','Simulator.Frontend','Simulator.RequestedQualification')){
        $assembly=Join-Path $ManagedBin ($name+'.dll');if(!(Test-Path -LiteralPath $assembly -PathType Leaf)){throw "Missing qualification assembly: $assembly"};[Reflection.Assembly]::LoadFrom($assembly)|Out-Null
    }
    Set-Location -LiteralPath $Checkout
    $providerBefore=(Get-FileHash -LiteralPath $Provider).Hash.ToLowerInvariant()
    # First-use shader compilation is recorded separately. It does not change
    # any deck, analysis, numerical control or comparison tolerance.
    $warmup=Get-Content -LiteralPath (Join-Path $Checkout 'examples/divider.json') -Raw|ConvertFrom-Json
    $warmup|Add-Member -NotePropertyName backend -NotePropertyValue intel -Force
    $client=[Simulator.Client.SimulationClient]::new($Engine)
    $deadline=[Threading.CancellationTokenSource]::new([TimeSpan]::FromSeconds(120))
    try {
        $clock=[Diagnostics.Stopwatch]::StartNew()
        $warmResponse=$client.RunJsonAsync(($warmup|ConvertTo-Json -Depth 32 -Compress),$deadline.Token).GetAwaiter().GetResult()
        if(!$warmResponse.Succeeded -or ![Simulator.RequestedQualification.RequestedComparison]::BackendIdentityMatches('intel',$warmResponse.ReadManifest().Backend)){throw 'Physical GPU warmup did not complete with the requested provider'}
        $warmResponse.Payload.GetRawText()|Set-Content -LiteralPath (Join-Path $OutputDirectory 'cold-warmup.response.json')
        [ordered]@{elapsed_seconds=$clock.Elapsed.TotalSeconds;response_file='cold-warmup.response.json';excluded_from_deck_counts=$true}|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $OutputDirectory 'cold-warmup.json')
    } finally {$deadline.Dispose()}
    $summaries=@()
    # Serialize complete programs to avoid manufacturing timing improvements
    # from overlapping CPU/GPU workloads. These reports include oracle overhead;
    # use separately timed request replay for per-transport comparisons.
    foreach($backend in @('lu','faer','intel')) {
        $out=Join-Path $OutputDirectory $backend
        $arguments=[string[]]@($Checkout,$Corpus,$Engine,$Ngspice,$Inventory,$out,'600',$backend)
        $exit=[Simulator.RequestedQualification.RequestedCorpus]::RunAsync($arguments).GetAwaiter().GetResult()
        $summaries+=@{backend=$backend;exit_code=$exit;summary=Join-Path $out 'summary.json'}
    }
    $providerAfter=(Get-FileHash -LiteralPath $Provider).Hash.ToLowerInvariant()
    [ordered]@{runtime_provenance_valid=($providerBefore -eq $providerAfter);engine_sha256=(Get-FileHash -LiteralPath $Engine).Hash.ToLowerInvariant();provider_sha256=$providerBefore;provider_after_sha256=$providerAfter;inventory_sha256=(Get-FileHash -LiteralPath $Inventory).Hash.ToLowerInvariant();runs=$summaries;limits=@('Complete ordinary requested-analysis comparison uses existing voltage/current/relative bounds.','GPU assembly/Newton/time control remain on the CPU; no CPU linear-solve fallback.','Run elapsed includes reference generation and archival; it is not a solver speed benchmark.')}|ConvertTo-Json -Depth 8|Set-Content -LiteralPath (Join-Path $OutputDirectory 'comparison.json')
    if($providerBefore -ne $providerAfter){throw 'Native provider changed during the comparison; all GPU agreement is invalidated'}
} finally {
    Set-Location -LiteralPath $prior.Path
    foreach($name in $saved.Keys){if($null -eq $saved[$name]){Remove-Item -LiteralPath ("Env:"+$name) -ErrorAction SilentlyContinue}else{[Environment]::SetEnvironmentVariable($name,$saved[$name],'Process')}}
}
