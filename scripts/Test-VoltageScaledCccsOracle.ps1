param(
 [Parameter(Mandatory)][string]$Repository,
 [Parameter(Mandatory)][string]$Output,
 [Parameter(Mandatory)][string]$ManagedBin,
 [Parameter(Mandatory)][string]$Engine,
 [Parameter(Mandatory)][string]$Ngspice
)
# Reuse the generated public client and existing bounded ASCII RAW reader.
# Compare every native signal at the actual bias, never substitute a frozen
# gain. This authored prerequisite campaign contributes no original-deck count.
if(!(Test-Path -LiteralPath (Split-Path -Parent $Output) -PathType Container)){throw 'Missing artifact parent'}
if(Test-Path -LiteralPath $Output){throw 'Fresh authored-oracle output required'}
New-Item -ItemType Directory -Path $Output|Out-Null
$ErrorActionPreference='Stop'
$r=$Repository;$o=$Output;$bin=$ManagedBin
foreach($p in @($r,$o,$bin)){if(!(Test-Path -LiteralPath $p -PathType Container)){throw "Missing $p"}}
$savedTemp=$env:TEMP;$savedTmp=$env:TMP
$env:TEMP=$o;$env:TMP=$o
foreach($name in 'Antlr4.Runtime.Standard','Simulator.Client','Simulator.Syntax','Simulator.Frontend','Simulator.RequestedQualification'){[Reflection.Assembly]::LoadFrom((Join-Path $bin ($name+'.dll')))|Out-Null}
$engine=$Engine;$oracle=$Ngspice
$client=[Simulator.Client.SimulationClient]::new($engine)
$elements=[Simulator.Client.Element[]]@(
 [Simulator.Client.Voltage]::new('Vsense',1,0,1),[Simulator.Client.Resistor]::new('Rsense',1,0,100),
 [Simulator.Client.Voltage]::new('Vcontrol',2,0,2),[Simulator.Client.VoltageScaledCccs]::new('product',3,0,2,0,'Vsense',2,0.25),
 [Simulator.Client.Resistor]::new('Rload',3,0,1000))
$circuit=[Simulator.Client.Circuit]::new([string[]]@('0','sense','ctrl','out'),$elements)
$rows=@();$saved=Get-Location
try{
 Set-Location -LiteralPath $o
 foreach($mode in 'op','ac_sense','ac_control'){
  $sense=if($mode -eq 'ac_sense'){1}else{0};$control=if($mode -eq 'ac_control'){1}else{0}
  $analysis=if($mode -eq 'op'){[Simulator.Client.OperatingPoint]::new()}else{[Simulator.Client.Ac]::new([double[]]@(1000),[Simulator.Client.AcSourcePhasor[]]@([Simulator.Client.AcSourcePhasor]::new('Vsense',$sense,0),[Simulator.Client.AcSourcePhasor]::new('Vcontrol',$control,0)))}
  $command=if($mode -eq 'op'){'op'}else{'ac lin 1 1000 1000'}
  $deck=@"
Voltage-scaled branch-current oracle, $mode
Vsense sense 0 dc=1 ac=$sense
Rsense sense 0 100
Vcontrol ctrl 0 dc=2 ac=$control
Bproduct out 0 i={2*i(Vsense)*(1+0.25*v(ctrl))}
Rload out 0 1000
.control
set filetype=ascii
$command
write oracle-$mode.raw all
quit 0
.endc
.end
"@
  Set-Content -LiteralPath "$o/oracle-$mode.cir" -Value $deck
  & $oracle -n -b "$o/oracle-$mode.cir" *> "$o/oracle-$mode.log"
  if($LASTEXITCODE){throw "Stock oracle failed $mode"}
  $raw=[Simulator.RequestedQualification.AsciiRaw]::Read((Get-Content -LiteralPath "$o/oracle-$mode.raw" -Raw))
  if($raw.Points.Length -ne 1){throw 'Unexpected native point count'}
  foreach($backend in 'lu','faer'){
   $request=[Simulator.Client.SimulationRequest]::new("product-$mode-$backend",$circuit,$analysis,$backend,$null)
   $json=[System.Text.Json.JsonSerializer]::Serialize($request,[Simulator.Client.SimulationRequest],[Simulator.Client.WireFormat]::Options)
   Set-Content -LiteralPath "$o/$mode-$backend.request.json" -Value $json
   $response=$client.RunAsync($request,[Threading.CancellationToken]::None).GetAwaiter().GetResult()
   Set-Content -LiteralPath "$o/$mode-$backend.response.json" -Value $response.Payload.GetRawText()
   if(!$response.Succeeded){throw "Typed native execution failed $mode/$backend"}
   $manifest=$response.ReadManifest();$point=if($mode -eq 'op'){$manifest.Samples[0].Values}else{$manifest.AcSamples[0].Values}
   $observables=@();for($i=0;$i -lt $raw.Variables.Length;$i++){
    $name=$raw.Variables[$i].Name
    if($name -eq 'frequency'){if($mode -eq 'op' -or $raw.Points[0][$i].Real -ne $manifest.AcSamples[0].FrequencyHz -or $raw.Points[0][$i].Imag -ne 0){throw 'Coordinate mismatch'};continue}
    $index=-1;for($j=0;$j -lt $manifest.Signals.Length;$j++){if($manifest.Signals[$j].Id.Equals($name,[StringComparison]::OrdinalIgnoreCase)){$index=$j;break}}
    if($index -lt 0){throw "Unmapped oracle observable $name"}
    $real=if($mode -eq 'op'){$point[$index]}else{$point[$index].Real};$imag=if($mode -eq 'op'){0}else{$point[$index].Imag}
    $absoluteError=[Math]::Sqrt([Math]::Pow($real-$raw.Points[0][$i].Real,2)+[Math]::Pow($imag-$raw.Points[0][$i].Imag,2))
    $absolute=if($name.StartsWith('i(')){1e-9}else{1e-6};$bound=$absolute+1e-4*[Math]::Sqrt([Math]::Pow($raw.Points[0][$i].Real,2)+[Math]::Pow($raw.Points[0][$i].Imag,2))
    if($absoluteError -gt $bound){throw "Native mismatch $mode/$backend/$name"}
    $observables+=@{signal=$name;absolute_error=$absoluteError;absolute_tolerance=$absolute;relative_tolerance=1e-4}
   }
   $rows+=@{mode=$mode;backend=$backend;native_agreed=$true;observables=$observables;request_sha256=(Get-FileHash -LiteralPath "$o/$mode-$backend.request.json").Hash.ToLowerInvariant()}
  }
 }
 # A semantic reference failure must remain structured, and a later valid
 # operation must still succeed through the same public client instance.
 $bad=Get-Content -LiteralPath "$o/op-lu.request.json" -Raw|ConvertFrom-Json -AsHashtable
 $bad.circuit.elements[3].control_id='missing'
 $fault=$client.RunJsonAsync(($bad|ConvertTo-Json -Depth 20),[Threading.CancellationToken]::None).GetAwaiter().GetResult()
 if($fault.Succeeded -or $fault.Complete){throw 'Bad branch control became successful'}
 $again=$client.RunAsync([Simulator.Client.SimulationRequest]::new('after-fault',$circuit,[Simulator.Client.OperatingPoint]::new(),'lu',$null),[Threading.CancellationToken]::None).GetAwaiter().GetResult()
 if(!$again.Succeeded){throw 'Client unusable after expected model failure'}
 @{rows=$rows;semantic_failure=($fault.Payload.GetRawText()|ConvertFrom-Json);valid_after_failure=$true;engine_sha256=(Get-FileHash -LiteralPath $engine).Hash.ToLowerInvariant();oracle_sha256=(Get-FileHash -LiteralPath $oracle).Hash.ToLowerInvariant();scope='Authored native/API prerequisite only, no original-deck count'}|ConvertTo-Json -Depth 20|Set-Content -LiteralPath "$o/authored-oracle.json"
}finally{Set-Location -LiteralPath $saved.Path;$env:TEMP=$savedTemp;$env:TMP=$savedTmp}
exit 0
