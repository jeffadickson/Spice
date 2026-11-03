<#
.SYNOPSIS
Runs the established Stryker.NET tool in a disposable semantic-stage snapshot.
.DESCRIPTION
All copied sources, generated grammar, build outputs and logs live under the
explicit output directory. This avoids rewriting or mutating the active checkout.
#>
[CmdletBinding()]
param([Parameter(Mandatory)][ValidateSet('Binding','Parameters','Resources','Bridge')][string]$Stage,
 [Parameter(Mandatory)][string]$OutputDirectory,[Parameter(Mandatory)][string]$AntlrJar,[string[]]$MutationPatterns)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
foreach($p in @($root,(Split-Path ([IO.Path]::GetFullPath($OutputDirectory)) -Parent))){if(!(Test-Path -LiteralPath $p -PathType Container)){throw "Missing directory $p"}}
if(!(Test-Path -LiteralPath $AntlrJar -PathType Leaf)){throw 'Missing pinned ANTLR jar'}
if(Test-Path -LiteralPath $OutputDirectory){throw 'Use a fresh mutation output directory'}
New-Item -ItemType Directory -Path $OutputDirectory|Out-Null
$snapshot=Join-Path $OutputDirectory 'source'
New-Item -ItemType Directory -Path $snapshot|Out-Null
foreach($name in @('dotnet-tools.json','global.json')){Copy-Item -LiteralPath (Join-Path $root $name) -Destination $snapshot}
$dotnet=Join-Path $snapshot 'dotnet';New-Item -ItemType Directory -Path $dotnet|Out-Null
foreach($project in $(if($Stage -eq 'Bridge'){@('Simulator.Syntax','Simulator.Client','Simulator.Frontend','Simulator.RequestedQualification','Simulator.Frontend.Tests')}else{@('Simulator.Syntax','Simulator.Syntax.Tests')})){
 $destination=Join-Path $dotnet $project;New-Item -ItemType Directory -Path $destination|Out-Null
 # Exclude mutable output directories even when another machine built in-place.
 Get-ChildItem -LiteralPath (Join-Path $root "dotnet/$project")|Where-Object Name -notin @('bin','obj','StrykerOutput')|ForEach-Object{Copy-Item -LiteralPath $_.FullName -Destination $destination -Recurse}
}
$tools=Join-Path $snapshot '.tools';New-Item -ItemType Directory -Path $tools|Out-Null
$antlr=Join-Path $tools 'antlr';New-Item -ItemType Directory -Path $antlr|Out-Null
Copy-Item -LiteralPath $AntlrJar -Destination (Join-Path $antlr 'antlr-4.13.1-complete.jar')
$files=switch($Stage){Binding{@('**/InstanceBinding.cs','**/InstanceExtraction.cs')} Parameters{@('**/ParameterEvaluator.cs')} Resources{@('**/ResourceResolver.cs')} Bridge{@('**/OperatingPointBridge.cs')}}
# Stryker.NET span braces use character offsets, not line numbers.
if($MutationPatterns){$files=@($MutationPatterns)}
$config=@{'stryker-config'=@{project=$(if($Stage -eq 'Bridge'){'Simulator.Frontend.csproj'}else{'Simulator.Syntax.csproj'});mutate=@($files);concurrency=2;'mutation-level'='Basic';reporters=@('json','cleartext');'additional-timeout'=5000;thresholds=@{high=80;low=60;break=0}}}
$configPath=Join-Path $snapshot 'mutations.json';$config|ConvertTo-Json -Depth 8|Set-Content -LiteralPath $configPath
$before=Get-Location
try{
 Set-Location -LiteralPath $snapshot
 $testProject=if($Stage -eq 'Bridge'){'dotnet/Simulator.Frontend.Tests/Simulator.Frontend.Tests.csproj'}else{'dotnet/Simulator.Syntax.Tests/Simulator.Syntax.Tests.csproj'}
 dotnet tool run dotnet-stryker -- --config-file mutations.json --test-project $testProject --output (Join-Path $OutputDirectory 'reports')
 if($LASTEXITCODE){throw "Stryker failed ($LASTEXITCODE); raw evidence: $OutputDirectory"}
}finally{Set-Location -LiteralPath $before.Path}
