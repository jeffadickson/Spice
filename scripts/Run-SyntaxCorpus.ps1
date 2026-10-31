<#
.SYNOPSIS
Builds the generated frontend and records syntax-only results for all selected samples.
.DESCRIPTION
Uses the existing tracked inventory, plus tracked repository decks. Does not run
a simulator or publish issues. CorpusRoot overrides machine-specific inventory
paths on another computer. Initialize-SyntaxParser.ps1 supplies the pinned tool.
#>
[CmdletBinding()]
param([string]$Inventory,[string]$CorpusRoot,[string]$Output)
$ErrorActionPreference='Stop'
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
dotnet @arguments
if($LASTEXITCODE -ne 0){throw "Syntax campaign failed; inspect $Output and console diagnostics"}
