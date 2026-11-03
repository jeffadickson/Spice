param([Parameter(Mandatory)][string]$OutputDirectory)
# Isolated reference-only rebuild. No global package cache or production binary
# is patched. Long CIL branches change transport width, never numerical formulas.
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$OutputDirectory=[IO.Path]::GetFullPath($OutputDirectory)
if(-not(Test-Path -LiteralPath $root -PathType Container)){throw 'Missing checkout'}
$parent=Split-Path -Parent ([IO.Path]::GetFullPath($OutputDirectory));if(-not(Test-Path -LiteralPath $parent -PathType Container)){throw 'Missing output parent'}
if(Test-Path -LiteralPath $OutputDirectory){throw 'Use a fresh reference output directory'}
New-Item -ItemType Directory -Path $OutputDirectory|Out-Null
$commit='37cefa9afe9b3cbc19615ba29026b0ad1b2d2003'
$archive=Join-Path $OutputDirectory 'behavioral.zip'
Invoke-WebRequest -Uri "https://codeload.github.com/SpiceSharp/SpiceSharpBehavioral/zip/$commit" -OutFile $archive
$archiveHash=(Get-FileHash -LiteralPath $archive).Hash.ToLowerInvariant()
if($archiveHash -ne '7ac1086758b93e866bb67f58f56bbf3afb642abeeecea7f8689ae710b42d5b55'){throw 'Pinned upstream archive identity differs'}
$expanded=Join-Path $OutputDirectory 'upstream';Expand-Archive -LiteralPath $archive -DestinationPath $expanded
$source=Join-Path $expanded "SpiceSharpBehavioral-$commit"
if(-not(Test-Path -LiteralPath $source -PathType Container)){throw 'Unexpected source archive layout'}
$patches=@(
 @{file='ILState.cs';sha='a719385a3992847aeea476c49429a14b2c357b8426756e113acfc6ce2b39e074'},
 @{file='ComplexILState.cs';sha='82cb414c9a160be09ce4227af51ee8daa61b41001b38d5e9072a8d90aacc2f73'})
$changes=@()
foreach($patch in $patches){
 $path=Join-Path $source ('SpiceSharpBehavioral/Builders/Functions/'+$patch.file)
 if(-not(Test-Path -LiteralPath $path -PathType Leaf)||(Get-FileHash -LiteralPath $path).Hash.ToLowerInvariant() -ne $patch.sha){throw 'Unexpected patch preimage'}
 $text=[IO.File]::ReadAllText($path);$count=[regex]::Matches($text,'OpCodes\.(B[a-zA-Z]+)_S').Count
 if($count -lt 1){throw 'No expected short branches'}
 [IO.File]::WriteAllText($path,[regex]::Replace($text,'OpCodes\.(B[a-zA-Z]+)_S','OpCodes.$1'))
 $changes+=@{file=$patch.file;original_sha256=$patch.sha;patched_sha256=(Get-FileHash -LiteralPath $path).Hash.ToLowerInvariant();branch_instructions=$count}
}
$license=Join-Path $source 'LICENSE';if(-not(Test-Path -LiteralPath $license -PathType Leaf)){throw 'Missing upstream license'}
$build=Join-Path $OutputDirectory 'reference-build';New-Item -ItemType Directory -Path $build|Out-Null
$escaped=[Security.SecurityElement]::Escape($source)
$project=@"
<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>netstandard2.0</TargetFramework><AssemblyName>SpiceSharpBehavioral</AssemblyName><Version>3.2.0</Version><AssemblyVersion>3.2.0.0</AssemblyVersion><LangVersion>latest</LangVersion><EnableDefaultCompileItems>false</EnableDefaultCompileItems><RestorePackagesWithLockFile>true</RestorePackagesWithLockFile></PropertyGroup><ItemGroup><Compile Include="$escaped/SpiceSharpBehavioral/**/*.cs" Exclude="$escaped/SpiceSharpBehavioral/**/obj/**;$escaped/SpiceSharpBehavioral/**/bin/**"/><EmbeddedResource Include="$escaped/SpiceSharpBehavioral/Properties/Resources.resx" LogicalName="SpiceSharpBehavioral.Properties.Resources.resources"/><PackageReference Include="SpiceSharp" Version="3.2.3"/><PackageReference Include="SpiceSharpGenerator" Version="1.0.7" PrivateAssets="all"/><PackageReference Include="System.Reflection.Emit.Lightweight" Version="4.7.0"/></ItemGroup></Project>
"@
$csproj=Join-Path $build 'Patched.csproj';[IO.File]::WriteAllText($csproj,$project)
$artifacts=Join-Path $OutputDirectory 'build'
dotnet build $csproj --artifacts-path $artifacts -p:ImportDirectoryBuildProps=false -p:ImportDirectoryBuildTargets=false *> (Join-Path $OutputDirectory 'reference-build.log')
if($LASTEXITCODE){throw 'Patched reference build failed; see reference-build.log'}
$dll=Join-Path $artifacts 'bin/Patched/debug/SpiceSharpBehavioral.dll';if(-not(Test-Path -LiteralPath $dll -PathType Leaf)){throw 'Missing patched assembly'}
$manifest=Join-Path $OutputDirectory 'patch-manifest.json'
[ordered]@{schema_version=1;repository='https://github.com/SpiceSharp/SpiceSharpBehavioral';commit=$commit;license='MIT';license_sha256=(Get-FileHash -LiteralPath $license).Hash.ToLowerInvariant();archive_sha256=$archiveHash;changes=$changes;dll=$dll;dll_sha256=(Get-FileHash -LiteralPath $dll).Hash.ToLowerInvariant();flavor='reference-only long-branch IL patch; equations unchanged';limits='Not a stock-package result, production dependency, full upstream test run or upstream release'}|ConvertTo-Json -Depth 8|Set-Content -LiteralPath $manifest
$workerProject=Join-Path $root 'evaluation/SpiceSharp.ThirdReference/SpiceSharp.ThirdReference.csproj'
if(-not(Test-Path -LiteralPath $workerProject -PathType Leaf)){throw 'Missing third-reference worker source'}
dotnet build $workerProject --artifacts-path (Join-Path $OutputDirectory 'worker') -p:RestoreLockedMode=true "-p:ReferenceBehavioralPath=$dll" *> (Join-Path $OutputDirectory 'worker-build.log')
if($LASTEXITCODE){throw 'Third-reference worker build failed'}
dotnet build $workerProject --artifacts-path (Join-Path $OutputDirectory 'stock-worker') -p:RestoreLockedMode=true *> (Join-Path $OutputDirectory 'stock-worker-build.log')
if($LASTEXITCODE){throw 'Stock reference worker build failed'}
[pscustomobject]@{Manifest=$manifest;Worker=Join-Path $OutputDirectory 'worker/bin/SpiceSharp.ThirdReference/debug/SpiceSharp.ThirdReference.dll';StockWorker=Join-Path $OutputDirectory 'stock-worker/bin/SpiceSharp.ThirdReference/debug/SpiceSharp.ThirdReference.dll'}
