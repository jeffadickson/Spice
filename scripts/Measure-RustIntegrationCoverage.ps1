param([Parameter(Mandatory)][string]$Repository,[Parameter(Mandatory)][string]$TargetDirectory,[Parameter(Mandatory)][string]$Output,[string]$Test='pspice_switch',[string[]]$AdditionalCrates=@(),[string]$WorkingDirectory='',[string[]]$Features=@())
# Instrument only the owned library, reusing uninstrumented vendor dependencies.
# Compile the integration test against that exact rlib. This avoids an unnecessary
# rebuild of all vendor crates and does not substitute a model/error shim.
$ErrorActionPreference='Stop'
foreach($p in @($Repository,$TargetDirectory)){if(-not(Test-Path -LiteralPath $p -PathType Container)){throw "Missing directory: $p"}}
if(!$WorkingDirectory){$WorkingDirectory=$Repository}
if(!(Test-Path -LiteralPath $WorkingDirectory -PathType Container)){throw 'Missing test working directory'}
$WorkingDirectory=(Resolve-Path -LiteralPath $WorkingDirectory).Path
if(Test-Path -LiteralPath $Output){throw 'Coverage output must be fresh'}
$parent=Split-Path -Parent $Output;if(-not(Test-Path -LiteralPath $parent -PathType Container)){throw 'Missing output parent'}
if($Test -notmatch '^[a-zA-Z0-9_]+$'){throw 'Invalid test name'}
foreach($feature in $Features){if($feature -notmatch '^[a-zA-Z0-9_-]+$'){throw 'Invalid Cargo feature name'}}
$cargoFeatures=@();$testFeatures=@()
if($Features.Count){$cargoFeatures=@('--features',($Features -join ','));foreach($feature in $Features){$testFeatures+=@('--cfg',('feature="'+$feature+'"'))}}
$inputTest=Join-Path $Repository "tests/$Test.rs";if(-not(Test-Path -LiteralPath $inputTest -PathType Leaf)){throw 'Missing integration test'}
New-Item -ItemType Directory -Path $Output|Out-Null
$saved=@{};foreach($name in @('CARGO_TARGET_DIR','CARGO_BUILD_JOBS','PROPTEST_RNG_SEED','LLVM_PROFILE_FILE')){$saved[$name]=[Environment]::GetEnvironmentVariable($name,'Process')}
$before=Get-Location
try{
 Set-Location -LiteralPath $Repository
 $env:CARGO_TARGET_DIR=$TargetDirectory;$env:CARGO_BUILD_JOBS='2';$env:PROPTEST_RNG_SEED='88320261103'
 $messages=@(cargo rustc --locked --lib @cargoFeatures --message-format=json -- -C instrument-coverage 2> (Join-Path $Output 'build.log'))
 if($LASTEXITCODE){throw 'Instrumented library build failed'}
 $messages|Set-Content -LiteralPath (Join-Path $Output 'build.jsonl')
 $graph=@($messages|ForEach-Object{$_|ConvertFrom-Json}|Where-Object {$_.reason -eq 'compiler-artifact'})
 $artifacts=@($graph|Where-Object {$_.target.name -eq 'circuit_simulator'})
 $library=@($artifacts.filenames|Where-Object {$_ -like '*.rlib'})
 if($library.Count -ne 1){throw 'Expected exact owned instrumented rlib'}
 $deps=Join-Path $TargetDirectory 'debug/deps'
 $proptest=@(Get-ChildItem -LiteralPath $deps -Filter 'libproptest-*.rlib' -File)
 if($proptest.Count -ne 1){throw 'Expected unique existing proptest rlib'}
 $json=@($graph|Where-Object {$_.target.name -eq 'serde_json'}|ForEach-Object {$_.filenames}|Where-Object {$_ -like '*.rlib'}|Select-Object -Unique)
 if($json.Count -ne 1){throw 'Expected exact current serde_json rlib'}
 $extra=@();foreach($crate in $AdditionalCrates){
  if($crate -notmatch '^[a-zA-Z0-9_]+$'){throw 'Invalid additional dependency name'}
  $dependency=@($graph|Where-Object {$_.target.name -eq $crate}|ForEach-Object {$_.filenames}|Where-Object {$_ -like '*.rlib'}|Select-Object -Unique)
  if(!$dependency.Count){$dependency=@(Get-ChildItem -LiteralPath $deps -Filter "lib$crate-*.rlib" -File|ForEach-Object FullName)}
  if($dependency.Count -ne 1){throw "Expected exact current dependency: $crate"}
  $extra+=@('--extern',"$crate=$($dependency[0])")
 }
 $binary=Join-Path $Output 'instrumented-tests.exe'
 & rustc --edition=2024 --test $inputTest --crate-name=spice_integration_coverage @testFeatures -C instrument-coverage --extern "circuit_simulator=$($library[0])" --extern "proptest=$($proptest[0].FullName)" --extern "serde_json=$($json[0])" @extra -L "dependency=$deps" -o $binary 2> (Join-Path $Output 'test-build.log')
 if($LASTEXITCODE){throw 'Instrumented integration test build failed'}
 $env:LLVM_PROFILE_FILE=Join-Path $Output '%m-%p.profraw'
 # Some tests use relative artifact paths. Keep their working tree explicitly
 # selectable so bulk test data can stay on E: without changing source lookup.
 Push-Location -LiteralPath $WorkingDirectory
 try{& $binary *> (Join-Path $Output 'test.log');if($LASTEXITCODE){throw 'Instrumented test failed'}}finally{Pop-Location}
 $sysroot=(& rustc --print sysroot).Trim();$tools=Join-Path $sysroot 'lib/rustlib/x86_64-pc-windows-msvc/bin'
 foreach($name in @('llvm-profdata.exe','llvm-cov.exe')){if(-not(Test-Path -LiteralPath (Join-Path $tools $name) -PathType Leaf)){throw 'Matching LLVM tooling unavailable'}}
 $profiles=@(Get-ChildItem -LiteralPath $Output -Filter '*.profraw' -File|ForEach-Object FullName)
 & (Join-Path $tools 'llvm-profdata.exe') merge -sparse @profiles -o (Join-Path $Output 'merged.profdata');if($LASTEXITCODE){throw 'Profile merge failed'}
 $raw=& (Join-Path $tools 'llvm-cov.exe') export $binary ('-instr-profile='+ (Join-Path $Output 'merged.profdata')) 2> (Join-Path $Output 'llvm.log')
 if($LASTEXITCODE){throw 'LLVM export failed'}
 [IO.File]::WriteAllText((Join-Path $Output 'raw.json'),($raw -join "`n"))
 $data=($raw -join "`n")|ConvertFrom-Json
 $owned=@($data.data[0].files|Where-Object {$_.filename.Replace('\','/').StartsWith($Repository.Replace('\','/').TrimEnd('/')+'/src/',[StringComparison]::OrdinalIgnoreCase)})
 if(-not $owned.Count){throw 'No exact owned source maps'}
 $metrics=@($owned|ForEach-Object {@{file=[IO.Path]::GetRelativePath($Repository,$_.filename).Replace('\','/');sha256=(Get-FileHash -LiteralPath $_.filename).Hash.ToLowerInvariant();lines=$_.summary.lines}})
 [ordered]@{tool='cargo rustc + rustc instrument-coverage + matching LLVM';rustc=(& rustc --version);test=$Test;features=$Features;environment_seed='88320261103';scope='Owned library exercised by named integration test; vendor/test source excluded';files=$metrics;branches=$null;limitations=@('Stable Rust instrumentation provides no measured branch coverage.','Other tests and executable main are outside this run.','A named test can pin its own seed, overriding the environment seed; retain actual test replay configuration alongside this report.')}|ConvertTo-Json -Depth 12|Set-Content -LiteralPath (Join-Path $Output 'summary.json')
}finally{Set-Location -LiteralPath $before.Path;foreach($name in $saved.Keys){if($null -eq $saved[$name]){Remove-Item -LiteralPath ("Env:"+$name) -ErrorAction SilentlyContinue}else{[Environment]::SetEnvironmentVariable($name,$saved[$name],'Process')}}}
