#requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$ViennaClRoot,
    [Parameter(Mandatory)][string]$OutputDirectory
)
$ErrorActionPreference='Stop'
if(!$IsWindows){throw 'This provider build is currently qualified on Windows x64 only.'}
if(!(Test-Path -LiteralPath $ViennaClRoot -PathType Container)){throw 'Missing pinned ViennaCL checkout'}
$ViennaClRoot=(Resolve-Path -LiteralPath $ViennaClRoot).Path
$pin=& git -c "safe.directory=$($ViennaClRoot.Replace('\','/'))" -C $ViennaClRoot rev-parse HEAD
if($LASTEXITCODE -or $pin -ne '77e9049d5622d46b6801a725917a931eda096f76'){throw 'ViennaCL source pin mismatch'}
$dirty=@(& git -c "safe.directory=$($ViennaClRoot.Replace('\','/'))" -C $ViennaClRoot status --porcelain --untracked-files=all)
if($LASTEXITCODE -or $dirty.Count){throw 'Pinned ViennaCL source must be clean, including untracked headers'}
$parent=Split-Path $OutputDirectory -Parent
if(!(Test-Path -LiteralPath $parent -PathType Container)){throw 'Missing output parent'}
if(Test-Path -LiteralPath $OutputDirectory){throw 'Output must be fresh'}
$OutputDirectory=[IO.Path]::GetFullPath($OutputDirectory)
if([IO.Path]::GetPathRoot($OutputDirectory) -ne 'E:\'){throw 'Keep provider build outputs on E:'}
New-Item -ItemType Directory -Path $OutputDirectory | Out-Null
. (Join-Path $PSScriptRoot 'HostTools.ps1')
Import-SpiceMsvc
$priorTemp=$env:TEMP;$priorTmp=$env:TMP
try {
    $env:TEMP=$OutputDirectory;$env:TMP=$OutputDirectory
    $driver=Join-Path $env:WINDIR 'System32/OpenCL.dll'
    if(!(Test-Path -LiteralPath $driver -PathType Leaf)){throw 'Missing system OpenCL loader'}
    # The installed ICD ships a DLL, not development import libraries. Generate
    # only a link-time import table from its public exports; never copy the DLL.
    $exports=& dumpbin.exe /exports $driver
    if($LASTEXITCODE){throw 'Cannot inspect OpenCL exports'}
    $names=@($exports | ForEach-Object {if($_ -match '^\s+\d+\s+[0-9A-F]+\s+[0-9A-F]+\s+(cl\w+)'){$Matches[1]}})
    if($names.Count -lt 20){throw 'Unexpected OpenCL export table'}
    $definition=Join-Path $OutputDirectory 'OpenCL.def'
    @('LIBRARY OpenCL.dll','EXPORTS')+$names | Set-Content -LiteralPath $definition -Encoding ascii
    $library=Join-Path $OutputDirectory 'OpenCL.lib'
    & lib.exe /nologo /machine:x64 "/def:$definition" "/out:$library"
    if($LASTEXITCODE){throw 'OpenCL import library generation failed'}
    $source=Join-Path (Split-Path $PSScriptRoot -Parent) 'native/intel-opencl'
    if(!(Test-Path -LiteralPath $source -PathType Container)){throw 'Missing owned native source'}
    & cmake -S $source -B $OutputDirectory -G Ninja -DCMAKE_BUILD_TYPE=Release "-DVIENNACL_ROOT=$ViennaClRoot" "-DOPENCL_LIBRARY=$library"
    if($LASTEXITCODE){throw 'Intel provider configure failed'}
    & cmake --build $OutputDirectory --parallel 1 --verbose
    if($LASTEXITCODE){throw 'Intel provider compilation failed'}
    Copy-Item -LiteralPath (Join-Path $ViennaClRoot 'LICENSE') -Destination (Join-Path $OutputDirectory 'VIENNACL-LICENSE.txt')
    $dll=Join-Path $OutputDirectory 'spice_intel_opencl.dll'
    if(!(Test-Path -LiteralPath $dll -PathType Leaf)){throw 'Native provider missing'}
    $headers=[ordered]@{};foreach($directory in @('viennacl','CL')){foreach($header in (Get-ChildItem -LiteralPath (Join-Path $ViennaClRoot $directory) -Recurse -File|Sort-Object FullName)){ $headers[[IO.Path]::GetRelativePath($ViennaClRoot,$header.FullName)]=(Get-FileHash -LiteralPath $header.FullName).Hash.ToLowerInvariant() }}
    [ordered]@{viennacl_commit=$pin;provider_sha256=(Get-FileHash -LiteralPath $dll).Hash.ToLowerInvariant();solver_source_sha256=(Get-FileHash -LiteralPath (Join-Path $source 'solver.cpp')).Hash.ToLowerInvariant();owned_lifecycle_overlay_sha256=(Get-FileHash -LiteralPath (Join-Path $OutputDirectory 'compat/viennacl/ocl/backend.hpp')).Hash.ToLowerInvariant();dependency_headers=$headers;configuration='Release /O2 /fp:precise; OpenCL FP64 GMRES plus owned context-release overlay; no CPU fallback';driver_loader_sha256=(Get-FileHash -LiteralPath $driver).Hash.ToLowerInvariant()} | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $OutputDirectory 'build-manifest.json')
} finally {$env:TEMP=$priorTemp;$env:TMP=$priorTmp}
