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
    [switch]$LegacyBytes,[switch]$RequireValidSamples,[switch]$CollectFeatures,[switch]$CollectDeclarations,[switch]$CollectSemantics,
    [string]$ArtifactsPath,[string]$AntlrJar,[string[]]$ResourceRoots)
$ErrorActionPreference='Stop'
if($RequireValidSamples -and $LegacyBytes){throw 'Acceptance requires a strict UTF-8 primary; legacy replay is automatic'}
$repository=Split-Path $PSScriptRoot -Parent
if(!(Test-Path -LiteralPath $repository -PathType Container)){throw 'Missing repository'}
if(!$Inventory){$Inventory=Join-Path $repository 'evidence/ngspice-corpus-tracked/20261031T055611Z-019f8631.json'}
if(!(Test-Path -LiteralPath $Inventory -PathType Leaf)){throw 'Missing inventory'}
if($CorpusRoot -and !(Test-Path -LiteralPath $CorpusRoot -PathType Container)){throw 'Missing corpus root'}
if(!$Output){
    $artifactRoot=$env:CODEX_ARTIFACT_ROOT
    if(!$artifactRoot -and $IsWindows){$artifactRoot='E:\Artifacts'}
    if(!$artifactRoot -or !(Test-Path -LiteralPath $artifactRoot -PathType Container)){throw 'Set an existing CODEX_ARTIFACT_ROOT or explicit output'}
    $Output=Join-Path $artifactRoot ('Spice-syntax-'+[DateTime]::UtcNow.ToString('yyyyMMddTHHmmssZ')+'-'+[guid]::NewGuid().ToString('N').Substring(0,8)+'.json')
}
$parent=Split-Path ([IO.Path]::GetFullPath($Output)) -Parent
if(!(Test-Path -LiteralPath $parent -PathType Container)){throw 'Output parent must exist'}
$project=Join-Path $repository 'dotnet/Simulator.Syntax.Corpus'
if(!(Test-Path -LiteralPath $project -PathType Container)){throw 'Missing corpus project'}
if(!$ArtifactsPath){$ArtifactsPath=Join-Path $parent ('syntax-build-'+[guid]::NewGuid().ToString('N'))}
if(!(Test-Path -LiteralPath $ArtifactsPath -PathType Container)){
    if(!(Test-Path -LiteralPath (Split-Path ([IO.Path]::GetFullPath($ArtifactsPath)) -Parent) -PathType Container)){throw 'Build output parent missing'}
    New-Item -ItemType Directory -Path $ArtifactsPath|Out-Null
}
if(!$AntlrJar){
    $existing=Join-Path $repository '.tools/antlr/antlr-4.13.1-complete.jar'
    if(Test-Path -LiteralPath $existing -PathType Leaf){$AntlrJar=$existing}
    else{
        & (Join-Path $PSScriptRoot 'Initialize-SyntaxParser.ps1') -OutputDirectory $ArtifactsPath
        $AntlrJar=Join-Path $ArtifactsPath 'antlr/antlr-4.13.1-complete.jar'
    }
}
if(!(Test-Path -LiteralPath $AntlrJar -PathType Leaf)){throw 'Missing ANTLR generator'}
$buildArguments=@('build',$project,'-p:RestoreLockedMode=true','--nologo',('-p:AntlrJar='+$AntlrJar),('-p:GrammarOutput='+ (Join-Path $ArtifactsPath 'grammar')))
if($ArtifactsPath){
    if(!(Test-Path -LiteralPath $ArtifactsPath -PathType Container)){throw 'ArtifactsPath must exist'}
    $buildArguments+=@('--artifacts-path',$ArtifactsPath)
}
dotnet @buildArguments
if($LASTEXITCODE -ne 0){throw 'Syntax build failed'}
$dll=if($ArtifactsPath){Join-Path $ArtifactsPath 'bin/Simulator.Syntax.Corpus/debug/Simulator.Syntax.Corpus.dll'}else{Join-Path $project 'bin/Debug/net10.0/Simulator.Syntax.Corpus.dll'}
$arguments=@($dll,$Inventory,$repository,$Output)
if($CorpusRoot){$arguments+=$CorpusRoot}
if($ResourceRoots){foreach($root in $ResourceRoots){if(!(Test-Path -LiteralPath $root -PathType Container)){throw "Missing resource root $root"}}}
$settings=@{
    SPICE_PARSE_WORKER_SECONDS=$WorkerSeconds; SPICE_PARSE_CAMPAIGN_MINUTES=$Minutes
    SPICE_PARSE_WORKERS=$Workers; SPICE_PARSE_MAX_BYTES=$MaxInputBytes
    SPICE_PARSE_MAX_TOKENS=$MaxTokens; SPICE_PARSE_LEGACY_BYTES=[int][bool]$LegacyBytes
    SPICE_PARSE_FEATURES=[int][bool]$CollectFeatures
    SPICE_PARSE_DECLARATIONS=[int][bool]$CollectDeclarations
    SPICE_PARSE_SEMANTICS=[int][bool]$CollectSemantics
    SPICE_PARSE_RESOURCE_ROOTS_JSON=$(if($ResourceRoots){ConvertTo-Json -InputObject @($ResourceRoots) -Compress}else{""})
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
        dotnet run --file (Join-Path $PSScriptRoot 'ValidateSyntaxAcceptance.cs') --artifacts-path (Join-Path $ArtifactsPath 'gates') -- $Output $catalog $Inventory $repository ($Output+'.acceptance.json') $replay
        if($LASTEXITCODE -ne 0){throw "Valid sample acceptance remains outstanding; inspect $Output.acceptance.json"}
    }
}finally{
    foreach($name in $saved.Keys){[Environment]::SetEnvironmentVariable($name,$saved[$name])}
}
