param([string]$Python=$env:SPICE_STRICTDOC_PYTHON)
# Sphinx is an explicit documentation dependency; do not alter global Python.
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
if(-not(Test-Path -LiteralPath $root -PathType Container)){throw 'Checkout directory unavailable.'}
if(-not $Python){
    $candidate=Join-Path $root $(if($IsWindows){'.tools/strictdoc/Scripts/python.exe'}else{'.tools/strictdoc/bin/python'})
    if(Test-Path -LiteralPath $candidate -PathType Leaf){$Python=$candidate}
}
if(-not $Python -or -not(Test-Path -LiteralPath $Python -PathType Leaf)){throw 'Pass -Python with an existing Python 3.12+ executable, or import the prepared host tools.'}
& $Python -c 'import sys; assert sys.version_info >= (3, 12)'
if($LASTEXITCODE -ne 0){throw 'Sphinx 9.1.0 requires Python 3.12 or newer.'}
$tools=Join-Path $root '.tools'
if(-not(Test-Path -LiteralPath $tools -PathType Container)){New-Item -ItemType Directory -Path $tools|Out-Null}
$environment=Join-Path $tools 'sphinx'
if(-not(Test-Path -LiteralPath $environment -PathType Container)){
    & $Python -m venv $environment
    if($LASTEXITCODE -ne 0){throw 'Sphinx environment creation failed.'}
}
$runner=Join-Path $environment $(if($IsWindows){'Scripts/python.exe'}else{'bin/python'})
if(-not(Test-Path -LiteralPath $runner -PathType Leaf)){throw 'Existing Sphinx environment is incomplete; preserve it for inspection.'}
Push-Location -LiteralPath $root
try {
    & $runner -m pip install -r docs/engineering/requirements.lock.txt
    if($LASTEXITCODE -ne 0){throw 'Pinned Sphinx restore failed.'}
    & dotnet run --file scripts/EngineeringDocs.cs -- --python $runner
    if($LASTEXITCODE -ne 0){throw 'Engineering documentation build failed.'}
} finally {Pop-Location}
