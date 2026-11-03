BeforeAll {
 $script:launcher=Join-Path $PSScriptRoot '../Run-VerifiedSoak.ps1'
 $sourceParent=$env:SPICE_SOAK_SOURCE_ROOT;$script:artifactParent=$env:SPICE_SOAK_TEST_ROOT
 foreach($path in @($sourceParent,$artifactParent)){if(-not(Test-Path -LiteralPath $path -PathType Container)){throw 'Existing source/artifact test roots required'}}
 # The tiny Git/source fixture stays NTFS because directory-junction creation is
 # itself under test. All child logs, manifests, work and target paths stay E:.
 $script:canonical=Join-Path $sourceParent ('soak-supervision-'+[guid]::NewGuid().ToString('N'))
 New-Item -ItemType Directory -Path $canonical|Out-Null
 $scripts=Join-Path $canonical 'scripts';New-Item -ItemType Directory -Path $scripts|Out-Null
 Set-Content -LiteralPath (Join-Path $canonical '.gitignore') -Value @('/work/','/target/','/.tools/')
 Set-Content -LiteralPath (Join-Path $scripts 'HostTools.ps1') -Value 'function Import-SpiceTools {param([string]$Root)}; function Import-SpiceMsvc {}'
 @'
param([double]$Minutes,[switch]$SkipMutation,[string]$FinishByUtc)
$root=Split-Path $PSScriptRoot -Parent
@{pid=$PID;minutes=$Minutes;finish_by_utc=$FinishByUtc;framework_work_root=$env:SPICE_FRAMEWORK_WORK_ROOT}|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $root 'work/child-started.json')
if($env:SPICE_SOAK_FIXTURE_MODE -eq 'slow'){Start-Sleep -Seconds 60}
exit 0
'@|Set-Content -LiteralPath (Join-Path $scripts 'Run-Endurance.ps1')
 & git -C $canonical init --quiet;& git -C $canonical add .;& git -C $canonical -c user.name=Contract -c user.email=contract@example.invalid commit --quiet -m fixture
 if($LASTEXITCODE){throw 'Source fixture commit failed'}
 $script:pinned=(& git -C $canonical rev-parse HEAD).Trim()
 New-Item -ItemType Directory -Path (Join-Path $canonical 'work')|Out-Null
 $script:supervisor=$env:SPICE_ENDURANCE_SUPERVISOR_DLL
 if(-not(Test-Path -LiteralPath $supervisor -PathType Leaf)){throw 'Build current Endurance DLL and set SPICE_ENDURANCE_SUPERVISOR_DLL'}
 $script:previousMode=$env:SPICE_SOAK_FIXTURE_MODE
 function New-Checkout([string]$name){$path=Join-Path $canonical "work/$name";& git -C $canonical worktree add --detach --quiet $path $pinned;if($LASTEXITCODE){throw 'Fixture checkout failed'};return $path}
}
AfterAll {$env:SPICE_SOAK_FIXTURE_MODE=$previousMode;Remove-Variable -Name SpiceSoakReportWrites -Scope Global -ErrorAction SilentlyContinue}
Describe 'Whole-child soak supervision' {
 It 'records a returned bootstrap and clean pinned source with E outputs' {
  $checkout=New-Checkout 'fast';$output=Join-Path $artifactParent ('soak-fast-'+[guid]::NewGuid().ToString('N'));$env:SPICE_SOAK_FIXTURE_MODE='fast'
  & $launcher -Checkout $checkout -CanonicalRepository $canonical -ExpectedCommit $pinned -ArtifactDirectory $output -Minutes .1 -SkipMutation
  $manifest=Get-Content -LiteralPath (Join-Path $output 'launch-manifest.json') -Raw|ConvertFrom-Json
  $manifest.state|Should -Be 'worker_returned';$manifest.source_commit|Should -Be $pinned;$manifest.source_dirty|Should -BeFalse
  Test-Path -LiteralPath (Join-Path $output 'work/child-started.json') -PathType Leaf|Should -BeTrue
  $childState=Get-Content -LiteralPath (Join-Path $output 'work/child-started.json') -Raw|ConvertFrom-Json
  $childState.framework_work_root|Should -Be (Join-Path $output 'fm')
  $manifest.framework_work_root|Should -Be $childState.framework_work_root
  $manifest.outer_started_utc|Should -Not -BeNullOrEmpty
  $manifest.effective_outer_deadline_utc|Should -Not -BeNullOrEmpty
  $manifest.test_finish_by_utc|Should -Not -BeNullOrEmpty
  # ConvertFrom-Json may materialize dates as DateTime. Cast directly rather
  # than formatting/parsing through a string that loses fractional seconds.
  $anchor=[DateTimeOffset]$manifest.outer_started_utc
  $outer=[DateTimeOffset]$manifest.effective_outer_deadline_utc
  $outer.UtcTicks|Should -Be $anchor.AddSeconds(6).UtcTicks
  ([DateTimeOffset]$childState.finish_by_utc).UtcTicks|Should -Be $outer.AddSeconds(-5).UtcTicks
  $manifest.test_finish_by_utc|Should -Be $childState.finish_by_utc
 }
 It 'caps a later explicit finish by the same relative-duration anchor' {
  $checkout=New-Checkout 'later-finish';$output=Join-Path $artifactParent ('soak-later-'+[guid]::NewGuid().ToString('N'));$env:SPICE_SOAK_FIXTURE_MODE='fast'
  $requested=[DateTimeOffset]::UtcNow.AddMinutes(5).ToString('o')
  & $launcher -Checkout $checkout -CanonicalRepository $canonical -ExpectedCommit $pinned -ArtifactDirectory $output -Minutes .1 -SkipMutation -FinishByUtc $requested
  $manifest=Get-Content -LiteralPath (Join-Path $output 'launch-manifest.json') -Raw|ConvertFrom-Json
  $childState=Get-Content -LiteralPath (Join-Path $output 'work/child-started.json') -Raw|ConvertFrom-Json
  $manifest.effective_outer_deadline_utc|Should -Not -BeNullOrEmpty
  $outer=[DateTimeOffset]$manifest.effective_outer_deadline_utc
  $outer.UtcTicks|Should -Be (([DateTimeOffset]$manifest.outer_started_utc).AddSeconds(6).UtcTicks)
  ($outer -lt [DateTimeOffset]$requested)|Should -BeTrue
  ([DateTimeOffset]$childState.finish_by_utc).UtcTicks|Should -Be $outer.AddSeconds(-5).UtcTicks
 }
 It 'preserves an earlier explicit absolute finish window' {
  $checkout=New-Checkout 'earlier-finish';$output=Join-Path $artifactParent ('soak-earlier-'+[guid]::NewGuid().ToString('N'));$env:SPICE_SOAK_FIXTURE_MODE='fast'
  $requested=[DateTimeOffset]::UtcNow.AddSeconds(15).ToString('o')
  & $launcher -Checkout $checkout -CanonicalRepository $canonical -ExpectedCommit $pinned -ArtifactDirectory $output -Minutes 1 -SkipMutation -FinishByUtc $requested
  $manifest=Get-Content -LiteralPath (Join-Path $output 'launch-manifest.json') -Raw|ConvertFrom-Json
  $childState=Get-Content -LiteralPath (Join-Path $output 'work/child-started.json') -Raw|ConvertFrom-Json
  $manifest.effective_outer_deadline_utc|Should -Not -BeNullOrEmpty
  ([DateTimeOffset]$manifest.effective_outer_deadline_utc).UtcTicks|Should -Be ([DateTimeOffset]$requested).UtcTicks
  ([DateTimeOffset]$childState.finish_by_utc).UtcTicks|Should -Be (([DateTimeOffset]$requested).AddSeconds(-5).UtcTicks)
 }
 It 'rejects a window consumed by the five-second cleanup reserve before dispatch' {
  $checkout=New-Checkout 'too-short';$output=Join-Path $artifactParent ('soak-short-'+[guid]::NewGuid().ToString('N'));$env:SPICE_SOAK_FIXTURE_MODE='slow'
  {& $launcher -Checkout $checkout -CanonicalRepository $canonical -ExpectedCommit $pinned -ArtifactDirectory $output -Minutes .02 -SkipMutation}|Should -Throw '*five-second cleanup reserve*'
  Test-Path -LiteralPath (Join-Path $output 'work/child-started.json')|Should -BeFalse
  $manifest=Get-Content -LiteralPath (Join-Path $output 'launch-manifest.json') -Raw|ConvertFrom-Json
  $manifest.state|Should -Be 'launcher_failed'
  $manifest.child_pid|Should -BeNullOrEmpty
 }
 It 'terminates a stuck bootstrap by the outer deadline and never claims completed tests' {
  $checkout=New-Checkout 'slow';$output=Join-Path $artifactParent ('soak-slow-'+[guid]::NewGuid().ToString('N'));$env:SPICE_SOAK_FIXTURE_MODE='slow'
  {& $launcher -Checkout $checkout -CanonicalRepository $canonical -ExpectedCommit $pinned -ArtifactDirectory $output -Minutes .1 -SkipMutation -FinishByUtc ([DateTimeOffset]::UtcNow.AddSeconds(30).ToString('o'))}|Should -Throw '*Outer soak deadline*'
  $manifest=Get-Content -LiteralPath (Join-Path $output 'launch-manifest.json') -Raw|ConvertFrom-Json
  $manifest.state|Should -Be 'launcher_failed';$manifest.error|Should -Match 'Outer soak deadline'
  Get-Process -Id $manifest.child_pid -ErrorAction SilentlyContinue|Should -BeNullOrEmpty
 }
 It 'uses the same actual-worker lock for primary and linked checkouts' {
  $checkout=New-Checkout 'lock-probe'
  $first=& dotnet $supervisor --lock-probe $canonical;$LASTEXITCODE|Should -Be 0
  $second=& dotnet $supervisor --lock-probe $checkout;$LASTEXITCODE|Should -Be 0
  $second|Should -Be $first
  $guard=[IO.FileStream]::new((Join-Path $canonical 'work/endurance.lock'),[IO.FileMode]::OpenOrCreate,[IO.FileAccess]::ReadWrite,[IO.FileShare]::None)
  try {& dotnet $supervisor --lock-probe $checkout *> (Join-Path $artifactParent 'held-canonical-lock-probe.log');$LASTEXITCODE|Should -Not -Be 0}finally{$guard.Dispose()}
 }
 It 'terminates the child if reporting fails immediately after dispatch' {
  $checkout=New-Checkout 'report-fault';$output=Join-Path $artifactParent ('soak-report-fault-'+[guid]::NewGuid().ToString('N'));$env:SPICE_SOAK_FIXTURE_MODE='slow'
  $global:SpiceSoakReportWrites=0
  Mock Set-Content {
   $global:SpiceSoakReportWrites++
   if($global:SpiceSoakReportWrites -eq 2){throw [IO.IOException]::new('Simulated report failure after child dispatch')}
   [IO.File]::WriteAllText($LiteralPath,($Value -join [Environment]::NewLine))
  } -ParameterFilter {$LiteralPath -match 'soak-report-fault-[^\\/]+[\\/]launch-manifest\.json$'}
  {& $launcher -Checkout $checkout -CanonicalRepository $canonical -ExpectedCommit $pinned -ArtifactDirectory $output -Minutes .1 -SkipMutation}|Should -Throw '*Simulated report failure*'
  $manifest=Get-Content -LiteralPath (Join-Path $canonical 'work/verified-soak-active.json') -Raw|ConvertFrom-Json
  $manifest.state|Should -Be 'launcher_failed'
  Get-Process -Id $manifest.child_pid -ErrorAction SilentlyContinue|Should -BeNullOrEmpty
 }
}
