BeforeAll {
 $script:runner=Join-Path $PSScriptRoot '../scripts/FrameworkMutations.cs'
 $parent=$env:SPICE_PATH_TEST_ROOT
 if(-not(Test-Path -LiteralPath $parent -PathType Container)){throw 'Set existing SPICE_PATH_TEST_ROOT on the artifact volume'}
 $script:physical=Join-Path $parent ('p-'+[guid]::NewGuid().ToString('N').Substring(0,8))
 New-Item -ItemType Directory -Path $physical|Out-Null
 # The alias lives on NTFS; the destination may be exFAT. The linked component
 # is deliberately an ancestor of the requested directory, not its last part.
 $temporary=[IO.Path]::GetTempPath()
 if(-not(Test-Path -LiteralPath $temporary -PathType Container)){throw 'NTFS temporary directory missing'}
 $script:aliasBase=Join-Path $temporary ('spice-path-'+[guid]::NewGuid().ToString('N'))
 New-Item -ItemType Directory -Path $aliasBase|Out-Null
 $script:aliasParent=Join-Path $aliasBase ('long-checkout-segment-'*8)
 New-Item -ItemType Directory -Path $aliasParent|Out-Null
 $script:alias=Join-Path $aliasParent 'artifact-junction'
 New-Item -ItemType Junction -Path $alias -Target $physical|Out-Null
 New-Item -ItemType Directory -Path (Join-Path $physical 'nested')|Out-Null
 $project=Join-Path $PSScriptRoot '../dotnet/Simulator.TransportFixture/Simulator.TransportFixture.csproj'
 & dotnet build $project --nologo --artifacts-path (Join-Path $physical 'build') -o (Join-Path $physical 'nested') *> (Join-Path $physical 'fixture-build.log')
 if($LASTEXITCODE){throw 'Real transport fixture build failed'}
}
AfterAll {
 # Remove only the NTFS junction itself; retain bulk evidence on E:.
 if(Test-Path -LiteralPath $alias){Remove-Item -LiteralPath $alias -Force}
 if(Test-Path -LiteralPath $aliasParent){Remove-Item -LiteralPath $aliasParent}
 if(Test-Path -LiteralPath $aliasBase){Remove-Item -LiteralPath $aliasBase}
}
Describe 'Physical mutation paths' -Skip:(-not $IsWindows) {
 It 'resolves an intermediate junction before choosing the managed build path' {
  $output=& dotnet run --file $runner -- --physical-path-self-test (Join-Path $alias 'nested') 2>&1|Out-String
  $LASTEXITCODE|Should -Be 0
  $output.Trim()|Should -Be ([IO.Path]::GetFullPath((Join-Path $physical 'nested')))
 }
 It 'refuses deep physical Windows roots before creating mutation outputs' {
  $deep=Join-Path $physical ('d'*120)
  New-Item -ItemType Directory -Path $deep|Out-Null
  $output=& dotnet run --file $runner -- --physical-path-self-test $deep 2>&1|Out-String
  $LASTEXITCODE|Should -Not -Be 0
  $output|Should -Match 'shorter physical mutation root'
 }
 It 'rejects missing roots rather than silently writing into the checkout' {
  & dotnet run --file $runner -- --physical-path-self-test (Join-Path $physical 'missing') *> $null
  $LASTEXITCODE|Should -Not -Be 0
 }
 It 'launches the real fixture after resolving a long logical checkout ancestor junction' {
  $logical=Join-Path $alias 'nested/Simulator.TransportFixture.exe'
  $logical.Length|Should -BeGreaterThan 260
  $failed=& dotnet run --file $runner -- --fixture-preflight-self-test $logical 2>&1|Out-String
  $LASTEXITCODE|Should -Not -Be 0
  $failed|Should -Match 'Win32Exception'
  $resolved=(& dotnet run --file $runner -- --physical-path-self-test (Join-Path $alias 'nested')|Out-String).Trim()
  $LASTEXITCODE|Should -Be 0
  $output=& dotnet run --file $runner -- --fixture-preflight-self-test (Join-Path $resolved 'Simulator.TransportFixture.exe') 2>&1|Out-String
  $LASTEXITCODE|Should -Be 0
  $output|Should -Match 'Real transport fixture launch passed'
 }
 It 'propagates a missing fixture launch failure' {
  & dotnet run --file $runner -- --fixture-preflight-self-test (Join-Path $physical 'missing.exe') *> $null
  $LASTEXITCODE|Should -Not -Be 0
 }
}
