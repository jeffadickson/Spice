<#
.SYNOPSIS
Builds the generated frontend and records syntax-only results for all selected samples.
.DESCRIPTION
Uses the existing tracked inventory, plus tracked repository decks. Does not run
a simulator or publish issues. CorpusRoot overrides machine-specific inventory
paths on another computer. Initialize-SyntaxParser.ps1 supplies the pinned tool.
#>
[CmdletBinding()]
param([string]$Inventory,[string]$CorpusRoot,[string]$Output,
    [ValidateRange(1,300)][int]$WorkerSeconds=20,
    [ValidateRange(1,1440)][int]$Minutes=30,
    [ValidateRange(1,4)][int]$Workers=1,
    [ValidateRange(1,67108864)][int]$MaxInputBytes=4194304,
    [ValidateRange(1,4000000)][int]$MaxTokens=2000000,
    [switch]$LegacyBytes,[switch]$RequireValidSamples)
$ErrorActionPreference='Stop'
if($RequireValidSamples -and $LegacyBytes){throw 'Acceptance requires a strict UTF-8 primary; legacy replay is automatic'}
$repository=Split-Path $PSScriptRoot -Parent
if(!(Test-Path -LiteralPath $repository -PathType Container)){throw 'Missing repository'}
if(!$Inventory){$Inventory=Join-Path $repository 'evidence/ngspice-corpus-tracked/20261031T055611Z-019f8631.json'}
if(!(Test-Path -LiteralPath $Inventory -PathType Leaf)){throw 'Missing inventory'}
if($CorpusRoot -and !(Test-Path -LiteralPath $CorpusRoot -PathType Container)){throw 'Missing corpus root'}
if(!$Output){
    $work=Join-Path $repository 'work'
    if(!(Test-Path -LiteralPath $work -PathType Container)){New-Item -ItemType Directory -Path $work | Out-Null}
    $Output=Join-Path $work ('syntax-corpus-'+[DateTime]::UtcNow.ToString('yyyyMMddTHHmmssZ')+'.json')
}
$parent=Split-Path ([IO.Path]::GetFullPath($Output)) -Parent
if(!(Test-Path -LiteralPath $parent -PathType Container)){throw 'Output parent must exist'}
$project=Join-Path $repository 'dotnet/Simulator.Syntax.Corpus'
if(!(Test-Path -LiteralPath $project -PathType Container)){throw 'Missing corpus project'}
& (Join-Path $PSScriptRoot 'Initialize-SyntaxParser.ps1')
dotnet build $project -p:RestoreLockedMode=true --nologo
if($LASTEXITCODE -ne 0){throw 'Syntax build failed'}
$arguments=@((Join-Path $project 'bin/Debug/net10.0/Simulator.Syntax.Corpus.dll'),$Inventory,$repository,$Output)
if($CorpusRoot){$arguments+=$CorpusRoot}
$settings=@{
    SPICE_PARSE_WORKER_SECONDS=$WorkerSeconds; SPICE_PARSE_CAMPAIGN_MINUTES=$Minutes
    SPICE_PARSE_WORKERS=$Workers; SPICE_PARSE_MAX_BYTES=$MaxInputBytes
    SPICE_PARSE_MAX_TOKENS=$MaxTokens; SPICE_PARSE_LEGACY_BYTES=[int][bool]$LegacyBytes
}
$saved=@{}
foreach($name in $settings.Keys){$saved[$name]=[Environment]::GetEnvironmentVariable($name);[Environment]::SetEnvironmentVariable($name,[string]$settings[$name])}
try{
    dotnet @arguments
    if($LASTEXITCODE -ne 0){throw "Syntax campaign failed; inspect $Output and console diagnostics"}
    if($RequireValidSamples){
        # Reuse the exact built DLLs, without rebuilding between primary and
        # replay. Only the explicit legacy input profile changes.
        $catalog=Join-Path $repository 'compat/ngspice/parser-input-adjudications.json'
        $entries=(Get-Content -LiteralPath $catalog -Raw | ConvertFrom-Json).entries
        $legacyPaths=@($entries | Where-Object disposition -eq 'legacy_encoded_deck' | ForEach-Object path)
        $subset=Get-Content -LiteralPath $Inventory -Raw | ConvertFrom-Json
        $subset.rows=@($subset.rows | Where-Object {$_.path -in $legacyPaths})
        if($subset.rows.Count -ne $legacyPaths.Count){throw 'Acceptance inventory does not contain the catalogued legacy samples'}
        $subsetPath=$Output+'.legacy-inventory.json'
        $subset | ConvertTo-Json -Depth 32 | Set-Content -LiteralPath $subsetPath
        $replay=$Output+'.legacy.json'
        $replayArguments=@($arguments[0],$subsetPath,$repository,$replay)
        if($CorpusRoot){$replayArguments+=$CorpusRoot}
        $env:SPICE_PARSE_LEGACY_BYTES='1'
        dotnet @replayArguments
        if($LASTEXITCODE -ne 0){throw "Legacy replay failed; inspect $replay"}
        dotnet run --file (Join-Path $PSScriptRoot 'ValidateSyntaxAcceptance.cs') -- $Output $catalog $Inventory $repository ($Output+'.acceptance.json') $replay
        if($LASTEXITCODE -ne 0){throw "Valid sample acceptance remains outstanding; inspect $Output.acceptance.json"}
    }
}finally{
    foreach($name in $saved.Keys){[Environment]::SetEnvironmentVariable($name,$saved[$name])}
}
