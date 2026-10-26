param([double]$Minutes=300,[string]$Seed='', [switch]$SkipMutation,[switch]$SelectedRegressions,[switch]$Background)
# Native shell orchestration only. C# owns the campaign budget and process supervision.
$ErrorActionPreference='Stop'
if (-not [double]::IsFinite($Minutes) -or $Minutes -le 0 -or $Minutes -gt 1440) { throw 'Minutes must be in (0,1440].' }
if ($Seed -and $Seed -notmatch '^\d{1,20}$') { throw 'Seed must be an unsigned decimal integer.' }
$root=Split-Path $PSScriptRoot -Parent
if(-not(Test-Path -LiteralPath $root -PathType Container)){throw 'Checkout directory unavailable.'}
if($SkipMutation -and $SelectedRegressions){throw 'Selected regressions conflict with skipped mutations.'}
Set-Location -LiteralPath $root
if ($Background) {
    foreach($relative in @('work','work/endurance-launch')) {
        $directory=Join-Path $root $relative
        if(-not(Test-Path -LiteralPath (Split-Path $directory -Parent) -PathType Container)){throw 'Launch directory parent unavailable.'}
        if(-not(Test-Path -LiteralPath $directory -PathType Container)){New-Item -ItemType Directory -Path $directory|Out-Null}
    }
    $stamp=[guid]::NewGuid().ToString('N')
    $arguments=@('-NoProfile','-File',('"'+$PSCommandPath+'"'),'-Minutes',$Minutes.ToString([cultureinfo]::InvariantCulture))
    if ($Seed) { $arguments+=@('-Seed',$Seed) }
    if ($SkipMutation) { $arguments+='-SkipMutation' }
    if ($SelectedRegressions) { $arguments+='-SelectedRegressions' }
    $start=@{FilePath=(Get-Command pwsh).Source; ArgumentList=$arguments; WorkingDirectory=(Get-Location).Path; PassThru=$true; RedirectStandardOutput=(Join-Path (Get-Location) "work/endurance-launch/$stamp.out.log"); RedirectStandardError=(Join-Path (Get-Location) "work/endurance-launch/$stamp.err.log")}
    if ($IsWindows) { $start.WindowStyle='Hidden' }
    $child=Start-Process @start
    "Launched endurance worker PID $($child.Id); launcher logs: work/endurance-launch/$stamp.*.log"
    exit 0
}
. (Join-Path $PSScriptRoot 'HostTools.ps1')
Import-SpiceTools -Root (Get-Location).Path
$pinned=(Select-String -LiteralPath rust-toolchain.toml -Pattern '^channel = "([^"]+)"').Matches.Groups[1].Value
if ((rustc --version) -notmatch ('^rustc '+[regex]::Escape($pinned)+'\s')) { throw 'Campaign compiler does not match rust-toolchain.toml.' }
if(-not $SkipMutation){ & (Join-Path $PSScriptRoot 'Setup-MutationTools.ps1') }
$arguments=@('run','--file','scripts/Endurance.cs','--','--minutes',$Minutes.ToString([cultureinfo]::InvariantCulture))
if ($Seed) { $arguments+=@('--seed',$Seed) }
if ($SkipMutation) { $arguments+='--skip-mutation' }
if ($SelectedRegressions) { $arguments+='--selected-regressions' }
& dotnet @arguments
exit $LASTEXITCODE
