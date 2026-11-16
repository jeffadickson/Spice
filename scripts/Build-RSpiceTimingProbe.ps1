#requires -Version 7.0
[CmdletBinding()]
param(
 [Parameter(Mandatory)][string]$Source,
 [Parameter(Mandatory)][string]$DependencyDirectory,
 [Parameter(Mandatory)][string]$WindowsImportDirectory,
 [Parameter(Mandatory)][string]$Output
)
$ErrorActionPreference='Stop'
foreach($dir in @($PSScriptRoot,$Source,$DependencyDirectory,$WindowsImportDirectory,(Split-Path $Output -Parent))){if(!(Test-Path -LiteralPath $dir -PathType Container)){throw "Missing directory: $dir"}}
$pin=git -c "safe.directory=$Source" -C $Source rev-parse HEAD
if($LASTEXITCODE -ne 0 -or $pin -ne '29c28a022625fd7cc2c56ead1cce2d3066663074'){throw 'Pinned RSpice source required'}
$library=@(Get-ChildItem -LiteralPath $DependencyDirectory -Filter 'librspice_core-*.rlib' -File)
if($library.Count -ne 1){throw 'Expected one independently pinned built core library; no implicit rebuild/download'}
if(!(Test-Path -LiteralPath (Join-Path $WindowsImportDirectory 'windows.lib') -PathType Leaf)){throw 'Missing Windows import library'}
$repo=Split-Path $PSScriptRoot -Parent
if(!(Test-Path -LiteralPath $repo -PathType Container)){throw 'Missing checkout'}
$helper=Join-Path $repo 'evaluation/RSpiceTimingProbe.rs'
if(!(Test-Path -LiteralPath $helper -PathType Leaf)){throw 'Missing integration source'}
if(Test-Path -LiteralPath $Output){throw 'Fresh executable output required'}
$root=if($env:CODEX_ARTIFACT_ROOT){$env:CODEX_ARTIFACT_ROOT}else{'E:/Artifacts'}
if(!(Test-Path -LiteralPath $root -PathType Container)){throw 'Missing artifact root'}
$prefix=[IO.Path]::TrimEndingDirectorySeparator([IO.Path]::GetFullPath($root))+[IO.Path]::DirectorySeparatorChar
if(![IO.Path]::GetFullPath($Output).StartsWith($prefix,[StringComparison]::OrdinalIgnoreCase)){throw 'Keep timing output on artifact drive'}
$oldTemp=$env:TEMP;$oldTmp=$env:TMP
try{
 $env:TEMP=Split-Path $Output -Parent;$env:TMP=$env:TEMP
 & rustc $helper --edition=2024 -C opt-level=3 --extern "rspice_core=$($library[0].FullName)" -L "dependency=$DependencyDirectory" -L "native=$WindowsImportDirectory" -o $Output
 if($LASTEXITCODE){throw 'Probe build failed; matching compiler/dependency graph required'}
 [ordered]@{source_commit=$pin;rustc=((& rustc --version)-join ' ');source_sha256=(Get-FileHash -LiteralPath $helper).Hash;library=$library[0].FullName;library_sha256=(Get-FileHash -LiteralPath $library[0].FullName).Hash;executable_sha256=(Get-FileHash -LiteralPath $Output).Hash;profile='Optimized integration glue; cached core profile must be recorded independently'}|ConvertTo-Json|Set-Content -LiteralPath ($Output+'.provenance.json')
}finally{$env:TEMP=$oldTemp;$env:TMP=$oldTmp}
