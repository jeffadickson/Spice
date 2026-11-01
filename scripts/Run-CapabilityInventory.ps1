<#
.SYNOPSIS
Inventories all accepted corpus inputs using the generated parser and emits a capability baseline.
.DESCRIPTION
Runs strict UTF-8 primary recognition plus the hash-pinned legacy replay. Raw
reports/builds stay under the selected artifact root. No simulation, include
loading, shell execution, issue publication or CI dispatch occurs in this script.
#>
[CmdletBinding()]
param([string]$Inventory,[string]$CorpusRoot,[string]$OutputDirectory,
    [ValidateRange(1,300)][int]$WorkerSeconds=90,
    [ValidateRange(1,1440)][int]$Minutes=30,
    [ValidateRange(1,4)][int]$Workers=2)
$ErrorActionPreference='Stop'
$repository=Split-Path $PSScriptRoot -Parent
if(!(Test-Path -LiteralPath $repository -PathType Container)){throw 'Repository missing'}
if(!$OutputDirectory){
    $root=$env:CODEX_ARTIFACT_ROOT
    if(!$root -or !(Test-Path -LiteralPath $root -PathType Container)){throw 'Set CODEX_ARTIFACT_ROOT to an existing writable directory or supply OutputDirectory'}
    $OutputDirectory=Join-Path $root ('Spice-capability-'+[DateTime]::UtcNow.ToString('yyyyMMddTHHmmssZ')+'-'+[guid]::NewGuid().ToString('N').Substring(0,8))
}
$OutputDirectory=[IO.Path]::GetFullPath($OutputDirectory)
$parent=Split-Path $OutputDirectory -Parent
if(!(Test-Path -LiteralPath $parent -PathType Container)){throw 'Output parent must exist'}
if(!(Test-Path -LiteralPath $OutputDirectory -PathType Container)){New-Item -ItemType Directory -Path $OutputDirectory | Out-Null}
$primary=Join-Path $OutputDirectory 'primary.json'
if(Test-Path -LiteralPath $primary){throw 'Use a fresh output directory; existing evidence will not be overwritten'}
$build=Join-Path $OutputDirectory 'build'
if(!(Test-Path -LiteralPath $build -PathType Container)){New-Item -ItemType Directory -Path $build | Out-Null}
$settings=@{Output=$primary;ArtifactsPath=$build;RequireValidSamples=$true;CollectFeatures=$true;WorkerSeconds=$WorkerSeconds;Minutes=$Minutes;Workers=$Workers}
if($Inventory){$settings.Inventory=$Inventory}
if($CorpusRoot){$settings.CorpusRoot=$CorpusRoot}
& (Join-Path $PSScriptRoot 'Run-SyntaxCorpus.ps1') @settings
$dll=Join-Path $build 'bin/Simulator.Syntax.Corpus/debug/Simulator.Syntax.Corpus.dll'
dotnet $dll --inventory $primary ($primary+'.legacy.json') (Join-Path $repository 'compat/ngspice/parser-input-adjudications.json') (Join-Path $repository 'compat/ngspice/capability-support.json') (Join-Path $OutputDirectory 'capabilities')
if($LASTEXITCODE -ne 0){throw "Capability inventory incomplete; inspect $OutputDirectory"}
