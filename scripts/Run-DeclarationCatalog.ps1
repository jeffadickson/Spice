<#
.SYNOPSIS
Runs the generated-tree declaration catalog profile and gates its evidence separately from syntax.
#>
[CmdletBinding()]
param([string]$Inventory,[string]$CorpusRoot,[string]$OutputDirectory,
    [ValidateRange(1,300)][int]$WorkerSeconds=90,[ValidateRange(1,1440)][int]$Minutes=30,
    [ValidateRange(1,4)][int]$Workers=2)
$ErrorActionPreference='Stop'
$repository=Split-Path $PSScriptRoot -Parent
if(!(Test-Path -LiteralPath $repository -PathType Container)){throw 'Repository missing'}
if(!$OutputDirectory){
    $root=$env:CODEX_ARTIFACT_ROOT
    if(!$root -or !(Test-Path -LiteralPath $root -PathType Container)){throw 'Set an existing CODEX_ARTIFACT_ROOT or supply OutputDirectory'}
    $OutputDirectory=Join-Path $root ('Spice-declarations-'+[DateTime]::UtcNow.ToString('yyyyMMddTHHmmssZ')+'-'+[guid]::NewGuid().ToString('N').Substring(0,8))
}
$OutputDirectory=[IO.Path]::GetFullPath($OutputDirectory)
if(!(Test-Path -LiteralPath (Split-Path $OutputDirectory -Parent) -PathType Container)){throw 'Output parent must exist'}
if(!(Test-Path -LiteralPath $OutputDirectory -PathType Container)){New-Item -ItemType Directory -Path $OutputDirectory | Out-Null}
$primary=Join-Path $OutputDirectory 'primary.json'
if(Test-Path -LiteralPath $primary){throw 'Use a fresh output directory'}
$build=Join-Path $OutputDirectory 'build'
if(!(Test-Path -LiteralPath $build -PathType Container)){New-Item -ItemType Directory -Path $build | Out-Null}
$settings=@{Output=$primary;ArtifactsPath=$build;RequireValidSamples=$true;CollectDeclarations=$true;WorkerSeconds=$WorkerSeconds;Minutes=$Minutes;Workers=$Workers}
if($Inventory){$settings.Inventory=$Inventory};if($CorpusRoot){$settings.CorpusRoot=$CorpusRoot}
& (Join-Path $PSScriptRoot 'Run-SyntaxCorpus.ps1') @settings
dotnet run --file (Join-Path $PSScriptRoot 'ValidateDeclarationCatalog.cs') -- $primary ($primary+'.legacy.json') ($primary+'.acceptance.json') (Join-Path $OutputDirectory 'declarations.json')
if($LASTEXITCODE){throw "Catalog evidence incomplete; inspect $OutputDirectory"}
