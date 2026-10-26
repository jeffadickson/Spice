param([Parameter(Mandatory)][string]$CheckoutPath,[string]$Repository='https://github.com/jeffadickson/Spice.git',[string]$Reference='main',[double]$Minutes=300,[string]$MachineLabel='lab',[switch]$NoInstall,[switch]$SkipNativeTools,[switch]$NoPublish,[switch]$SkipMutation,[switch]$SkipCoverage)
# Only an explicitly owned clone is reused; arbitrary local checkouts are refused.
$ErrorActionPreference='Stop'
if (-not [double]::IsFinite($Minutes) -or $Minutes -le 0 -or $Minutes -gt 1440) { throw 'Minutes must be in (0,1440].' }
if ($MachineLabel -notmatch '^[a-zA-Z0-9][a-zA-Z0-9_-]{0,47}$') { throw 'Invalid machine label.' }
if ($Reference -notmatch '^[a-zA-Z0-9][a-zA-Z0-9._/-]*$' -or $Reference.Contains('..')) { throw 'Invalid source reference.' }
$uri=$null
if ([Uri]::TryCreate($Repository,[UriKind]::Absolute,[ref]$uri) -and $uri.UserInfo) { throw 'Use Git credential helpers, not credentials embedded in the repository URL.' }
function Invoke-RemoteGit([string[]]$Arguments) { & git @Arguments; if ($LASTEXITCODE -ne 0) { throw 'Git checkout operation failed.' } }
if (-not (Get-Command git -ErrorAction SilentlyContinue)) {
    if ($NoInstall) { throw 'Git is required.' }
    if ($IsWindows) { & winget install --id Git.Git --exact --source winget --silent --accept-package-agreements --accept-source-agreements; if ($LASTEXITCODE -ne 0) { throw 'Git installation failed.' }; $env:Path='C:/Program Files/Git/cmd;'+$env:Path }
    elseif ($IsLinux -and (Get-Command apt-get -ErrorAction SilentlyContinue)) { & sudo apt-get update; if ($LASTEXITCODE -ne 0) { throw 'apt update failed.' }; & sudo apt-get install -y git; if ($LASTEXITCODE -ne 0) { throw 'Git installation failed.' } }
    else { throw 'Install Git before invoking this bootstrap on this platform.' }
}
$checkout=[IO.Path]::GetFullPath($CheckoutPath)
$marker=Join-Path $checkout '.git/spice-remote-owned'
if (Test-Path -LiteralPath $checkout) {
    if (-not (Test-Path -LiteralPath $marker)) { throw 'Refusing to reuse a directory not created by this remote-run bootstrap.' }
    $url=& git -C $checkout remote get-url origin
    if ($LASTEXITCODE -ne 0 -or $url -ne $Repository) { throw 'Existing clone origin does not match the requested repository.' }
    $changes=& git -C $checkout status --porcelain
    if ($LASTEXITCODE -ne 0 -or $changes) { throw 'Dedicated remote checkout has local changes; preserve them before reuse.' }
} else {
    Invoke-RemoteGit @('clone','--no-checkout','--',$Repository,$checkout)
    Set-Content -LiteralPath $marker -Value 'Spice remote qualification dedicated clone v1'
}
Invoke-RemoteGit @('-C',$checkout,'fetch','--','origin',$Reference)
Invoke-RemoteGit @('-C',$checkout,'checkout','--detach','FETCH_HEAD')
Set-Location -LiteralPath $checkout
& ./scripts/Setup-Host.ps1 -Install:(-not $NoInstall) -Coverage:(-not $SkipCoverage) -SkipNativeTools:$SkipNativeTools
if (-not $?) { throw 'Host setup failed.' }
$arguments=@('run','--file','scripts/RemoteQualification.cs','--','--minutes',$Minutes.ToString([cultureinfo]::InvariantCulture),'--label',$MachineLabel)
if ($NoPublish) { $arguments+='--no-publish' }
if ($SkipMutation) { $arguments+='--skip-mutation' }
if ($SkipCoverage) { $arguments+='--skip-coverage' }
& dotnet @arguments
exit $LASTEXITCODE
