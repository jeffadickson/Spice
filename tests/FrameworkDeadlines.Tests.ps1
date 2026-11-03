BeforeAll {
 $parent=$env:SPICE_PATH_TEST_ROOT
 if(-not(Test-Path -LiteralPath $parent -PathType Container)){throw 'Set existing SPICE_PATH_TEST_ROOT'}
 $script:run=Join-Path $parent ('deadline-'+[guid]::NewGuid().ToString('N').Substring(0,8))
 New-Item -ItemType Directory -Path $run|Out-Null
 $script:root=Split-Path $PSScriptRoot -Parent
 $runnerSource=Join-Path $root 'scripts/FrameworkMutations.cs'
 & dotnet build $runnerSource --nologo --artifacts-path (Join-Path $run 'runner-build') -o (Join-Path $run 'runner') *> (Join-Path $run 'runner-build.log')
 if($LASTEXITCODE){throw 'Runner build failed'}
 $script:runner=Join-Path $run 'runner/FrameworkMutations.exe'
 $fixtureSource=Join-Path $PSScriptRoot 'fixtures/FrameworkDeadlineFixture/FrameworkDeadlineFixture.csproj'
 & dotnet build $fixtureSource --nologo --artifacts-path (Join-Path $run 'fixture-build') -o (Join-Path $run 'fixture') *> (Join-Path $run 'fixture-build.log')
 if($LASTEXITCODE){throw 'Deadline fixture build failed'}
 $script:fixture=Join-Path $run 'fixture/Simulator.TransportFixture.exe'
 $script:previous=@{}
 foreach($name in @('SPICE_PREFLIGHT_PROBE_PID','SPICE_PREFLIGHT_PROBE_CHILD_PID','SPICE_PREFLIGHT_PROBE_MODE','SPICE_FRAMEWORK_WORK_ROOT','SPICE_CARGO_MUTANTS')){$previous[$name]=[Environment]::GetEnvironmentVariable($name)}
 function Reset-Probe([string]$mode){
  $env:SPICE_PREFLIGHT_PROBE_MODE=$mode
  $env:SPICE_PREFLIGHT_PROBE_PID=Join-Path $run ([guid]::NewGuid().ToString('N')+'.parent.pid')
  $env:SPICE_PREFLIGHT_PROBE_CHILD_PID=Join-Path $run ([guid]::NewGuid().ToString('N')+'.child.pid')
 }
 function Assert-Reaped([string]$marker){
  Test-Path -LiteralPath $marker -PathType Leaf|Should -BeTrue
  $ownedId=[int](Get-Content -LiteralPath $marker -Raw)
  Get-Process -Id $ownedId -ErrorAction SilentlyContinue|Should -BeNullOrEmpty
 }
}
AfterAll {
 # A failing regression must not leak its deliberately idle child.
 foreach($marker in (Get-ChildItem -LiteralPath $run -Filter '*.pid')){
  $ownedId=[int](Get-Content -LiteralPath $marker.FullName -Raw)
  $owned=Get-Process -Id $ownedId -ErrorAction SilentlyContinue
  if($null -ne $owned -and $owned.Path -eq $fixture){Stop-Process -Id $ownedId -Force}
 }
 foreach($name in $previous.Keys){[Environment]::SetEnvironmentVariable($name,$previous[$name])}
}
Describe 'Framework shared deadlines' -Skip:(-not $IsWindows) {
 It 'rejects a zero preflight budget before dispatching the real child' {
  Reset-Probe 'delay'
  $output=& $runner --fixture-preflight-self-test $fixture 0 2>&1|Out-String
  $LASTEXITCODE|Should -Not -Be 0
  $output|Should -Match 'preflight budget expired'
  Test-Path -LiteralPath $env:SPICE_PREFLIGHT_PROBE_PID|Should -BeFalse
 }
 It 'bounds a delayed real fixture by the shared near-zero budget and reaps it' {
  Reset-Probe 'delay';$clock=[Diagnostics.Stopwatch]::StartNew()
  $output=& $runner --fixture-preflight-self-test $fixture .25 2>&1|Out-String
  $LASTEXITCODE|Should -Not -Be 0
  $output|Should -Match 'preflight deadline'
  $clock.Elapsed.TotalSeconds|Should -BeLessThan 3
  Assert-Reaped $env:SPICE_PREFLIGHT_PROBE_PID
 }
 It 'bounds inherited output after parent exit and reaps the owned pipe holder' {
  Reset-Probe 'hold';$clock=[Diagnostics.Stopwatch]::StartNew()
  $output=& $runner --fixture-preflight-self-test $fixture 1.5 2>&1|Out-String
  $LASTEXITCODE|Should -Not -Be 0
  $output|Should -Match 'preflight deadline'
  $clock.Elapsed.TotalSeconds|Should -BeLessThan 4
  Assert-Reaped $env:SPICE_PREFLIGHT_PROBE_PID
  Assert-Reaped $env:SPICE_PREFLIGHT_PROBE_CHILD_PID
 }
 It 'refuses new tool dispatch after campaign expiry instead of attempting a missing executable' {
  $env:SPICE_FRAMEWORK_WORK_ROOT=$run
  $env:SPICE_CARGO_MUTANTS=Join-Path $run 'not-a-tool.exe'
  $output=& $runner --tool rust --minutes .000000001 2>&1|Out-String
  $LASTEXITCODE|Should -Not -Be 0
  $summaryPath=[regex]::Match($output,'(?m)^Framework summary: (.+)\r?$').Groups[1].Value.Trim()
  Test-Path -LiteralPath $summaryPath -PathType Leaf|Should -BeTrue
  $failure=Get-Content -LiteralPath (Join-Path (Split-Path $summaryPath -Parent) 'failure.log') -Raw
  $failure|Should -Match 'tool budget expired before dispatch'
  $failure|Should -Not -Match 'Win32Exception'
 }
}
