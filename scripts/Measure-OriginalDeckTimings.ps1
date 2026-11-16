#requires -Version 7.0
[CmdletBinding()]
param(
 [Parameter(Mandatory)][string]$Summary,
 [Parameter(Mandatory)][string]$Corpus,
 [Parameter(Mandatory)][string]$Engine,
 [Parameter(Mandatory)][string]$Ngspice,
 [Parameter(Mandatory)][string]$RspiceProbe,
 [Parameter(Mandatory)][string]$Output,
 [ValidateRange(1,5)][int]$Repetitions=3,
 [switch]$OnlyAgreed
)
$ErrorActionPreference='Stop'
foreach($dir in @($PSScriptRoot,$Corpus,(Split-Path $Output -Parent))){if(!(Test-Path -LiteralPath $dir -PathType Container)){throw "Missing directory: $dir"}}
foreach($file in @($Summary,$Engine,$Ngspice,$RspiceProbe)){if(!(Test-Path -LiteralPath $file -PathType Leaf)){throw "Missing file: $file"}}
$artifact=if($env:CODEX_ARTIFACT_ROOT){$env:CODEX_ARTIFACT_ROOT}else{'E:/Artifacts'}
if(!(Test-Path -LiteralPath $artifact -PathType Container)){throw 'Existing artifact root required'}
$prefix=[IO.Path]::TrimEndingDirectorySeparator([IO.Path]::GetFullPath($artifact))+[IO.Path]::DirectorySeparatorChar
if(![IO.Path]::GetFullPath($Output).StartsWith($prefix,[StringComparison]::OrdinalIgnoreCase)){throw 'Timing artifacts must stay under configured artifact root'}
$priorTemp=$env:TEMP;$priorTmp=$env:TMP
try{
 $env:TEMP=Split-Path $Output -Parent;$env:TMP=$env:TEMP
 Add-Type -Path (Join-Path $PSScriptRoot 'Measure-OriginalDeckTimings.cs')
 [OriginalDeckTimings]::Run($Summary,$Corpus,$Engine,$Ngspice,$RspiceProbe,$Output,$Repetitions,$OnlyAgreed.IsPresent)
}finally{$env:TEMP=$priorTemp;$env:TMP=$priorTmp}
