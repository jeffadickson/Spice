param([Parameter(Mandatory)][string]$Configuration,[Parameter(Mandatory)][DateTimeOffset]$FinishByUtc,[int]$Minutes=300,[switch]$ValidateOnly)
# A scheduled invocation consumes the same immutable cohort, never a fresh shard.
$ErrorActionPreference='Stop'
if(!(Test-Path -LiteralPath $Configuration -PathType Leaf)){throw 'Missing durable configuration'}
$c=Get-Content -LiteralPath $Configuration -Raw|ConvertFrom-Json
foreach($d in @($c.artifacts,$c.canonical_repository)){if(!(Test-Path -LiteralPath $d -PathType Container)){throw "Missing directory: $d"}}
if($Minutes -lt 1 -or $Minutes -gt 1440){throw 'Minutes must be 1..1440'}
$driver=Join-Path $PSScriptRoot 'Run-MutationBacklog.ps1'
& $driver -Configuration $Configuration -ValidateOnly
if($ValidateOnly){return}
if($FinishByUtc -le [DateTimeOffset]::UtcNow){throw 'Window expired; no work launched'}
$lock=Join-Path $c.canonical_repository 'work/endurance.lock'
try{$probe=[IO.File]::Open($lock,'OpenOrCreate','ReadWrite','None')}catch [IO.IOException]{Write-Output 'Active campaign preserved; no duplicate launched';return}
$probe.Dispose() # The driver acquires and retains the actual exclusive lease.
# The one-time legacy migration is explicitly tied to the original frozen config.
$c|Add-Member -NotePropertyName legacy_configuration_path -NotePropertyValue ([IO.Path]::GetFullPath($Configuration)) -Force
$c|Add-Member -NotePropertyName legacy_configuration_sha256 -NotePropertyValue ((Get-FileHash -LiteralPath $Configuration).Hash.ToLowerInvariant()) -Force
$c.minutes=$Minutes;$c.finish_by_utc=$FinishByUtc.ToString('o')
$invocation=Join-Path $c.artifacts ('resume-'+[DateTimeOffset]::UtcNow.ToString('yyyyMMddTHHmmssfffffffZ')+'.json')
[IO.File]::WriteAllText($invocation,($c|ConvertTo-Json -Depth 12))
& $driver -Configuration $invocation
