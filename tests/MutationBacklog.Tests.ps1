Describe 'Durable mutation backlog' {
 BeforeAll {
  $repo=Split-Path $PSScriptRoot -Parent
  $driver=Join-Path $repo 'scripts/Run-MutationBacklog.ps1'
  $resume=Join-Path $repo 'scripts/Resume-MutationBacklog.ps1'
  $root=$env:CODEX_ARTIFACT_ROOT
  if(!(Test-Path -LiteralPath $root -PathType Container)){throw 'Existing E: artifact root required'}
 }
 BeforeEach {
  $fixture=Join-Path $root ('queue-test-'+[guid]::NewGuid().ToString('N'))
  foreach($child in @('','source','artifacts','artifacts/temp','framework','canonical','canonical/work')){New-Item -ItemType Directory -Path (Join-Path $fixture $child) -Force|Out-Null}
  $pending=Join-Path $fixture 'pending.json'; '["src/a.rs:1:1: replace true with false"]'|Set-Content -LiteralPath $pending
  $helper=Join-Path $fixture 'helper.dll';'not invoked'|Set-Content -LiteralPath $helper
  $fixtureInput=Join-Path $fixture 'source/input.txt';'original'|Set-Content -LiteralPath $fixtureInput
  $config=[ordered]@{source=Join-Path $fixture 'source';artifacts=Join-Path $fixture 'artifacts';framework_work_root=Join-Path $fixture 'framework';canonical_repository=Join-Path $fixture 'canonical';pending_file=$pending;pending_sha256=(Get-FileHash $pending).Hash.ToLowerInvariant();framework_helper=$helper;helper_sha256=(Get-FileHash $helper).Hash.ToLowerInvariant();input_hashes=@{'input.txt'=(Get-FileHash $fixtureInput).Hash.ToLowerInvariant()};source_commit='0123456789012345678901234567890123456789';source_dirty=$false;qualification_overlay=@();minutes=1;finish_by_utc=$null;seed='1';cargo_mutants='unused';nuget_packages=$root}
  $configuration=Join-Path $fixture 'config.json'
  function Write-Config {$config|ConvertTo-Json -Depth 8|Set-Content -LiteralPath $configuration}
  Write-Config
 }
 It 'validates a frozen cohort without acquiring a lease or starting a helper' {
  (& $driver -Configuration $configuration -ValidateOnly|ConvertFrom-Json).candidates|Should -Be 1
  Test-Path (Join-Path $fixture 'canonical/work/endurance.lock')|Should -BeFalse
 }
 It 'rejects changed source inputs' {
  'changed'|Set-Content -LiteralPath $fixtureInput
  {& $driver -Configuration $configuration -ValidateOnly}|Should -Throw '*Source input changed*'
 }
 It 'rejects changed pending selection' {
  '[]'|Set-Content -LiteralPath $pending
  {& $driver -Configuration $configuration -ValidateOnly}|Should -Throw '*selection hash mismatch*'
 }
 It 'rejects a changed helper' {
  'changed'|Set-Content -LiteralPath $helper
  {& $driver -Configuration $configuration -ValidateOnly}|Should -Throw '*helper hash mismatch*'
 }
 It 'rejects duplicate cohort names' {
  '["src/a.rs:1:1: replace true with false","src/a.rs:1:1: replace true with false"]'|Set-Content -LiteralPath $pending
  $config.pending_sha256=(Get-FileHash $pending).Hash.ToLowerInvariant();Write-Config
  {& $driver -Configuration $configuration -ValidateOnly}|Should -Throw '*duplicate*'
 }
 It 'rejects unsafe names' {
  '["../escape:1:1: bad"]'|Set-Content -LiteralPath $pending
  $config.pending_sha256=(Get-FileHash $pending).Hash.ToLowerInvariant();Write-Config
  {& $driver -Configuration $configuration -ValidateOnly}|Should -Throw '*Unsafe*'
 }
 It 'preserves an active campaign' {
  $lease=[IO.File]::Open((Join-Path $fixture 'canonical/work/endurance.lock'),'OpenOrCreate','ReadWrite','None')
  try{(& $resume -Configuration $configuration -FinishByUtc ([DateTimeOffset]::UtcNow.AddMinutes(1)))|Should -Contain 'Active campaign preserved; no duplicate launched'}finally{$lease.Dispose()}
 }
 It 'refuses an expired window' {
  {& $resume -Configuration $configuration -FinishByUtc ([DateTimeOffset]::UtcNow.AddMinutes(-1))}|Should -Throw '*Window expired*'
 }
 It 'recovers <Outcome> after a checkpoint interruption without starting completed work' -TestCases @(@{Outcome='CaughtMutant'},@{Outcome='MissedMutant'},@{Outcome='Unviable'},@{Outcome='Timeout'}) {
  param($Outcome)
  $run=Join-Path $fixture 'framework/20261118-recovered'
  New-Item -ItemType Directory -Path (Join-Path $run 'rust/mutants.out') -Force|Out-Null
  $selection=Join-Path $fixture 'artifacts/selection-recovery.json';Copy-Item -LiteralPath $pending -Destination $selection
  @{campaign_identity=(& $driver -Configuration $configuration -ValidateOnly|ConvertFrom-Json).campaign_identity;run=$run;selection_sha256=(Get-FileHash -LiteralPath $selection).Hash.ToLowerInvariant()}|ConvertTo-Json|Set-Content -LiteralPath ($selection+'.manifest.json')
  @{rust_selection_sha256=(Get-FileHash $selection).Hash.ToLowerInvariant()}|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $run 'summary.json')
  @{outcomes=@(@{scenario=@{Mutant=@{name='src/a.rs:1:1: replace true with false'}};summary=$Outcome;log_path='log';diff_path='diff'},@{scenario=@{Mutant=@{name='src/other.rs:2:2: outside'}};summary='MissedMutant'})}|ConvertTo-Json -Depth 8|Set-Content -LiteralPath (Join-Path $run 'rust/mutants.out/outcomes.json')
  $originalLocation=Get-Location
  try{& $driver -Configuration $configuration; & $driver -Configuration $configuration}finally{Set-Location $originalLocation}
  $state=Get-Content -LiteralPath (Join-Path $fixture 'artifacts/summary.json') -Raw|ConvertFrom-Json
  $state.completed_candidates|Should -Be 1;$state.unattempted|Should -Be 0
  $state.status|Should -Be 'completed_needs_findings_review'
  $state.outcome_counts[0].Name|Should -Be $Outcome  # Legacy migration is explicit and bound to the original frozen config.
  $state.PSObject.Properties.Remove('campaign_identity')
  $state|ConvertTo-Json -Depth 9|Set-Content -LiteralPath (Join-Path $fixture 'artifacts/summary.json')
  & $resume -Configuration $configuration -FinishByUtc ([DateTimeOffset]::UtcNow.AddMinutes(1))|Out-Null
  $migrated=Get-Content -LiteralPath (Join-Path $fixture 'artifacts/summary.json') -Raw|ConvertFrom-Json
  $migrated.completed_candidates|Should -Be 1
  $migrated.campaign_identity|Should -Not -BeNullOrEmpty
  # Z-suffixed JSON dates become UTC DateTime values in PowerShell 7.6.
  # Reparsing their culture-formatted text would lose UTC and extend the window.
  $expectedDeadline=[DateTimeOffset]::UtcNow.AddMinutes(3)
  $config.minutes=60;$config.finish_by_utc=$expectedDeadline.UtcDateTime.ToString('o');Write-Config
  & $driver -Configuration $configuration
  $timed=Get-Content -LiteralPath (Join-Path $fixture 'artifacts/summary.json') -Raw|ConvertFrom-Json
  [Math]::Abs((([DateTimeOffset]$timed.deadline_utc)-$expectedDeadline).TotalSeconds)|Should -BeLessThan 0.01
  # A different replay seed is a distinct campaign, even with the same names.
  $config.seed='2';Write-Config
  {& $driver -Configuration $configuration}|Should -Throw '*provenance mismatch*'
 }
}
