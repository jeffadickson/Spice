param([switch]$Install,[switch]$Coverage,[switch]$SkipNativeTools)
# Host setup is shell/package-manager orchestration. Required upstream .NET/Rust
# installers are the runtime exceptions to the C#/F# scripting default.
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
Set-Location -LiteralPath $root
. (Join-Path $PSScriptRoot 'HostTools.ps1')
Import-SpiceTools -Root $root
New-Item -ItemType Directory -Force .tools/downloads,work/setup | Out-Null
$acquisitions=@()
function Get-SetupDownload([string]$Uri,[string]$Name) {
    $path=Join-Path $root ".tools/downloads/$Name"
    Invoke-WebRequest -Uri $Uri -OutFile $path
    $script:acquisitions += [ordered]@{uri=$Uri;sha256=(Get-FileHash $path -Algorithm SHA256).Hash.ToLowerInvariant()}
    return $path
}
function Invoke-SetupCommand([string]$File,[string[]]$Arguments) {
    & $File @Arguments
    if ($LASTEXITCODE -ne 0) { throw "Setup command failed: $File (exit $LASTEXITCODE)" }
}
if (-not ($IsWindows -or $IsLinux)) { throw 'This setup profile supports Windows x64 and Ubuntu/Debian Linux x64.' }
if ([Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture -ne 'X64') { throw 'This setup profile currently requires x64.' }
$nativeReady=$true
if ($IsLinux) { foreach ($command in @('gcc','make','pkg-config','curl','git','python3')) { if (-not (Get-Command $command -ErrorAction SilentlyContinue)) { $nativeReady=$false } } }
if ($Install -and $IsLinux -and -not $nativeReady) {
    if (-not (Get-Command apt-get -ErrorAction SilentlyContinue)) { throw 'Automated Linux package setup currently requires Ubuntu/Debian apt-get.' }
    $packages=@('build-essential','pkg-config','curl','ca-certificates','git','python3','python3-venv','libicu-dev','libssl-dev','zlib1g-dev')
    if ((& id -u) -eq '0') { Invoke-SetupCommand apt-get @('update'); Invoke-SetupCommand apt-get (@('install','-y')+$packages) }
    else { Invoke-SetupCommand sudo @('apt-get','update'); Invoke-SetupCommand sudo (@('apt-get','install','-y')+$packages) }
}
if (-not (Get-Command git -ErrorAction SilentlyContinue)) {
    if (-not $Install) { throw 'Git is missing; rerun Setup-Host.ps1 -Install.' }
    if ($IsWindows) { Invoke-SetupCommand winget @('install','--id','Git.Git','--exact','--source','winget','--silent','--accept-package-agreements','--accept-source-agreements'); $env:PATH='C:/Program Files/Git/cmd;'+$env:PATH }
}
if ($IsWindows -and -not $SkipNativeTools) {
    Import-SpiceMsvc
    if (-not (Get-Command link.exe -ErrorAction SilentlyContinue)) {
        if (-not $Install) { throw 'Visual C++ Build Tools/Windows SDK are missing. Rerun -Install or use -SkipNativeTools for an existing GNU toolchain.' }
        Invoke-SetupCommand winget @('install','--id','Microsoft.VisualStudio.2022.BuildTools','--exact','--source','winget','--silent','--accept-package-agreements','--accept-source-agreements','--override','--quiet --wait --norestart --add Microsoft.VisualStudio.Workload.VCTools --includeRecommended')
        Import-SpiceMsvc
        if (-not (Get-Command link.exe -ErrorAction SilentlyContinue)) { throw 'MSVC setup needs completion/restart or administrator approval; linker is still unavailable.' }
    }
}
$sdk=(Get-Content -Raw global.json|ConvertFrom-Json).sdk.version
$sdkVersion=$null
if (Get-Command dotnet -ErrorAction SilentlyContinue) { $sdkVersion=(& dotnet --version 2>$null); if ($LASTEXITCODE -ne 0) { $sdkVersion=$null } }
if (-not $sdkVersion) {
    if (-not $Install) { throw "The SDK selected by global.json ($sdk) is missing." }
    $directory=Join-Path $root '.tools/dotnet'
    if ($IsWindows) { $installer=Get-SetupDownload 'https://dot.net/v1/dotnet-install.ps1' 'dotnet-install.ps1'; Invoke-SetupCommand pwsh @('-NoProfile','-File',$installer,'-Version',$sdk,'-InstallDir',$directory,'-NoPath') }
    else { $installer=Get-SetupDownload 'https://dot.net/v1/dotnet-install.sh' 'dotnet-install.sh'; Invoke-SetupCommand bash @($installer,'--version',$sdk,'--install-dir',$directory,'--no-path') }
    Import-SpiceTools -Root $root
}
$pinned=(Select-String -LiteralPath rust-toolchain.toml -Pattern '^channel = "([^"]+)"').Matches.Groups[1].Value
if (-not (Get-Command rustup -ErrorAction SilentlyContinue)) {
    if (-not $Install) { throw 'Rustup is missing; rerun -Install.' }
    $target=if($IsWindows){'x86_64-pc-windows-msvc'}else{'x86_64-unknown-linux-gnu'}
    $name=if($IsWindows){'rustup-init.exe'}else{'rustup-init'}
    $uri="https://static.rust-lang.org/rustup/dist/$target/$name"
    $installer=Get-SetupDownload $uri $name
    $checksumResponse=Invoke-WebRequest "$uri.sha256"
    $checksumText=if($checksumResponse.Content -is [byte[]]){[Text.Encoding]::UTF8.GetString($checksumResponse.Content)}else{[string]$checksumResponse.Content}
    $expected=($checksumText -split '\s+')[0].Trim().ToLowerInvariant()
    if ($expected -notmatch '^[0-9a-f]{64}$' -or (Get-FileHash $installer -Algorithm SHA256).Hash.ToLowerInvariant() -ne $expected) { throw 'Rustup installer SHA-256 verification failed.' }
    $env:CARGO_HOME=Join-Path $root '.tools/rust/cargo'; $env:RUSTUP_HOME=Join-Path $root '.tools/rust/rustup'
    if (-not $IsWindows) { Invoke-SetupCommand chmod @('+x',$installer) }
    Invoke-SetupCommand $installer @('-y','--no-modify-path','--profile','minimal','--default-toolchain','none','--default-host',$target)
    Import-SpiceTools -Root $root
}
if ($Install) { Invoke-SetupCommand rustup @('toolchain','install',$pinned,'--profile','minimal','--component','rustfmt,clippy,llvm-tools-preview') }
$rust=& rustc --version
if ($LASTEXITCODE -ne 0 -or $rust -notmatch ('^rustc '+[regex]::Escape($pinned)+'\s')) { throw 'Rust compiler does not match rust-toolchain.toml.' }
$python=if($IsWindows){'python'}else{'python3'}
$venv=Join-Path $root '.tools/strictdoc'
$venvPython=Join-Path $venv $(if($IsWindows){'Scripts/python.exe'}else{'bin/python'})
$existing=Join-Path $root '../../work/strictdoc-env/Scripts/python.exe'
if (-not (Test-Path $venvPython) -and $env:SPICE_STRICTDOC_PYTHON -and (Test-Path $env:SPICE_STRICTDOC_PYTHON)) { $venvPython=$env:SPICE_STRICTDOC_PYTHON }
if (-not (Test-Path $venvPython) -and (Test-Path $existing)) { $venvPython=(Resolve-Path $existing).Path }
if (-not (Test-Path $venvPython)) {
    if (-not $Install) { throw 'StrictDoc runtime is missing; rerun -Install.' }
    if (-not (Get-Command $python -ErrorAction SilentlyContinue)) {
        if ($IsWindows) { Invoke-SetupCommand winget @('install','--id','Python.Python.3.13','--exact','--source','winget','--silent','--accept-package-agreements','--accept-source-agreements'); $python=Join-Path $env:LOCALAPPDATA 'Programs/Python/Python313/python.exe' }
        else { throw 'python3 is required for StrictDoc.' }
    }
    Invoke-SetupCommand $python @('-m','venv',$venv)
    Invoke-SetupCommand $venvPython @('-m','pip','install','strictdoc==0.30.4')
}
$env:SPICE_STRICTDOC_PYTHON=$venvPython
Invoke-SetupCommand $venvPython @('-c','from strictdoc.backend.sdoc.reader import SDReader; from importlib.metadata import version; assert version("strictdoc")=="0.30.4"')
if ($Coverage -and $Install) { $coverageToolchain=if($IsWindows){'nightly-x86_64-pc-windows-msvc'}else{'nightly'}; Invoke-SetupCommand rustup @('toolchain','install',$coverageToolchain,'--profile','minimal','--component','llvm-tools-preview') }
[ordered]@{status='ready';platform=[Runtime.InteropServices.RuntimeInformation]::OSDescription;rustc=$rust;dotnet=(& dotnet --version);strictdoc='0.30.4';native_tools_skipped=[bool]$SkipNativeTools;coverage_requested=[bool]$Coverage;acquisitions=$acquisitions} | ConvertTo-Json -Depth 6 | Set-Content work/setup/host.json
Write-Host 'Host dependencies verified. Configuration applies to this process and its children.'
