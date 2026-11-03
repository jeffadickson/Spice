BeforeAll {
 $script:launcher=Join-Path $PSScriptRoot '../Run-VerifiedSoak.ps1'
 $parent=$env:SPICE_SOAK_TEST_ROOT
 if(-not(Test-Path -LiteralPath $parent -PathType Container)){throw 'Set existing SPICE_SOAK_TEST_ROOT'}
 $script:fixture=Join-Path $parent ('soak-contract-'+[guid]::NewGuid().ToString('N'))
 New-Item -ItemType Directory -Path $fixture|Out-Null
 # exFAT has no owner identity. Trust only this newly created fixture for the
 # test process; do not weaken global Git configuration or use a wildcard.
 $script:oldConfigCount=$env:GIT_CONFIG_COUNT
 $script:configIndex=if($env:GIT_CONFIG_COUNT){[int]$env:GIT_CONFIG_COUNT}else{0}
 [Environment]::SetEnvironmentVariable("GIT_CONFIG_KEY_$configIndex",'safe.directory','Process')
 [Environment]::SetEnvironmentVariable("GIT_CONFIG_VALUE_$configIndex",$fixture.Replace('\','/'),'Process')
 $env:GIT_CONFIG_COUNT=($configIndex+1).ToString()
 & git -C $fixture init --quiet
 & git -C $fixture -c user.name=Contract -c user.email=contract@example.invalid commit --allow-empty --quiet -m fixture
 $script:commit=(& git -C $fixture rev-parse HEAD).Trim()
 $script:output=Join-Path $parent ('unused-soak-'+[guid]::NewGuid().ToString('N'))
 New-Item -ItemType Directory -Path (Join-Path $fixture 'work')|Out-Null
 # Preserve the clean-source invariant while using the same ignored lock path.
 $exclude=Join-Path $fixture '.git/info/exclude';Add-Content -LiteralPath $exclude -Value '/work/'
}
AfterAll {
 [Environment]::SetEnvironmentVariable("GIT_CONFIG_KEY_$configIndex",$null,'Process')
 [Environment]::SetEnvironmentVariable("GIT_CONFIG_VALUE_$configIndex",$null,'Process')
 $env:GIT_CONFIG_COUNT=$oldConfigCount
}
Describe 'Verified checkpoint soak boundary' {
 It 'validates a clean exact commit without creating output' {
  $finish=[DateTimeOffset]::UtcNow.AddHours(2).ToString('o')
  $r=& $launcher -Checkout $fixture -CanonicalRepository $fixture -ExpectedCommit $commit -ArtifactDirectory $output -FinishByUtc $finish -ValidateOnly
  $r.State|Should -Be 'validated_only'
  $r.FinishByUtc|Should -Be $finish
  Test-Path -LiteralPath $output|Should -BeFalse
 }
 It 'rejects a mismatching verified source' {
  {& $launcher -Checkout $fixture -CanonicalRepository $fixture -ExpectedCommit ('0'*40) -ArtifactDirectory $output -ValidateOnly}|Should -Throw '*does not match*'
 }
 It 'rejects malformed commit and nonfinite duration' {
  {& $launcher -Checkout $fixture -CanonicalRepository $fixture -ExpectedCommit 'main' -ArtifactDirectory $output -ValidateOnly}|Should -Throw
  {& $launcher -Checkout $fixture -CanonicalRepository $fixture -ExpectedCommit $commit -ArtifactDirectory $output -Minutes ([double]::NaN) -ValidateOnly}|Should -Throw
  {& $launcher -Checkout $fixture -CanonicalRepository $fixture -ExpectedCommit $commit -ArtifactDirectory $output -FinishByUtc 'not-a-date' -ValidateOnly}|Should -Throw
  {& $launcher -Checkout $fixture -CanonicalRepository $fixture -ExpectedCommit $commit -ArtifactDirectory $output -FinishByUtc ([DateTimeOffset]::UtcNow.AddMinutes(-1).ToString('o')) -ValidateOnly}|Should -Throw '*expired*'
 }
 It 'rejects dirty source without discarding it' {
  $file=Join-Path $fixture 'untracked.txt';Set-Content -LiteralPath $file -Value 'preserve'
  try {{& $launcher -Checkout $fixture -CanonicalRepository $fixture -ExpectedCommit $commit -ArtifactDirectory $output -ValidateOnly}|Should -Throw '*must be clean*';Get-Content -LiteralPath $file|Should -Be 'preserve'}finally{Remove-Item -LiteralPath $file}
 }
 It 'preserves an existing canonical campaign and creates no new run' {
  $lock=Join-Path $fixture 'work/endurance.lock'
  $guard=[IO.FileStream]::new($lock,[IO.FileMode]::OpenOrCreate,[IO.FileAccess]::ReadWrite,[IO.FileShare]::None)
  try {$r=& $launcher -Checkout $fixture -CanonicalRepository $fixture -ExpectedCommit $commit -ArtifactDirectory $output;$r.State|Should -Be 'existing_campaign_preserved';Test-Path -LiteralPath $output|Should -BeFalse}finally{$guard.Dispose()}
 }
 It 'rejects another repository as the canonical lock owner' {
  $other=Join-Path $fixture 'work/other';New-Item -ItemType Directory -Path $other|Out-Null;& git -C $other init --quiet
  {& $launcher -Checkout $fixture -CanonicalRepository $other -ExpectedCommit $commit -ArtifactDirectory $output -ValidateOnly}|Should -Throw '*different repository*'
 }
}
