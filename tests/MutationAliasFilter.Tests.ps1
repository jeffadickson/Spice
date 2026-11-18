Describe 'Pinned cargo-mutants alias workaround' {
 It 'filters impossible alias constructors while retaining Result and arithmetic mutations' {
  $repo=Split-Path $PSScriptRoot -Parent
  $root=$env:CODEX_ARTIFACT_ROOT
  if(!(Test-Path -LiteralPath $root -PathType Container)){throw 'Existing E: artifact root required'}
  $tool=$env:SPICE_CARGO_MUTANTS
  if(!(Test-Path -LiteralPath $tool -PathType Leaf)){throw 'Pinned cargo-mutants required'}
  $fixture=Join-Path $root ('alias-fixture-'+[guid]::NewGuid().ToString('N'))
  foreach($part in @('','src','.cargo')){New-Item -ItemType Directory -Path (Join-Path $fixture $part)|Out-Null}
  @'
[package]
name="alias-qualification"
version="0.1.0"
edition="2024"
'@|Set-Content -LiteralPath (Join-Path $fixture 'Cargo.toml')
  @'
type SimResult<T> = Result<T, ()>;
pub fn aliased() -> SimResult<f64> { Ok(2.) }
pub fn standard() -> Result<f64, ()> { Ok(2.) }
pub fn arithmetic(x:f64) -> f64 { x*2. }
'@|Set-Content -LiteralPath (Join-Path $fixture 'src/lib.rs')
  Copy-Item -LiteralPath (Join-Path $repo '.cargo/mutants.toml') -Destination (Join-Path $fixture '.cargo/mutants.toml')
  $before=(& $tool mutants --dir $fixture --list --json --no-config|ConvertFrom-Json)
  $LASTEXITCODE|Should -Be 0
  $after=(& $tool mutants --dir $fixture --list --json|ConvertFrom-Json)
  $LASTEXITCODE|Should -Be 0
  $removed=@($before|Where-Object {$_.name -notin $after.name})
  $removed.Count|Should -BeGreaterThan 0
  foreach($item in $removed){$item.name|Should -Match '-> SimResult<.* with SimResult::(new|from|from_iter)\('}
  @($after|Where-Object {$_.function.function_name -eq 'standard'}).Count|Should -BeGreaterThan 0
  @($after|Where-Object {$_.function.function_name -eq 'arithmetic' -and $_.replacement -eq '/'}).Count|Should -Be 1
  # Compile the three generator shapes to confirm the classification, rather
  # than merely trusting their spelling. The unmutated control must build.
  $manifest=Join-Path $fixture 'Cargo.toml'
  $target=Join-Path $fixture 'target'
  & cargo check --offline --manifest-path $manifest --target-dir $target *> (Join-Path $fixture 'control.log')
  $LASTEXITCODE|Should -Be 0
  foreach($body in @('SimResult::new()','SimResult::from(1.0)','SimResult::from_iter([1.0])')){
   ('type SimResult<T> = Result<T, ()>; pub fn invalid() -> SimResult<f64> { '+$body+' }')|Set-Content -LiteralPath (Join-Path $fixture 'src/lib.rs')
   $log=Join-Path $fixture ('invalid-'+[guid]::NewGuid().ToString('N')+'.log')
   & cargo check --offline --manifest-path $manifest --target-dir $target *> $log
   $LASTEXITCODE|Should -Not -Be 0
   (Get-Content -LiteralPath $log -Raw)|Should -Match 'error\[E(0308|0599|0271|0277)\]'
  }
 }
}
