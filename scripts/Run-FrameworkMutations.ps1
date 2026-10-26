param([double]$Minutes=60,[string]$Shard='0/1',[string]$RustFile='src/*.rs')
# Keep argument boundaries intact; C# validates budgets, filters and shards.
$ErrorActionPreference='Stop'
Set-Location (Split-Path $PSScriptRoot -Parent)
. ./scripts/HostTools.ps1
Import-SpiceTools -Root (Get-Location).Path
& ./scripts/Setup-MutationTools.ps1
dotnet run --file scripts/FrameworkMutations.cs -- --minutes $Minutes.ToString([cultureinfo]::InvariantCulture) --shard $Shard --rust-file $RustFile
exit $LASTEXITCODE
