param(
 [Parameter(Mandatory)][string]$Repository,
 [Parameter(Mandatory)][string]$Output,
 [Parameter(Mandatory)][string]$SchemaTools,
 [Parameter(Mandatory)][string]$Request
)
# Exercise the maintained validator on a charge companion extracted from a real
# typed request. This is schema evidence, not numerical/model qualification.
$ErrorActionPreference='Stop'
foreach($p in @($Repository,$SchemaTools,(Split-Path -Parent $Output))){if(!(Test-Path -LiteralPath $p -PathType Container)){throw "Missing $p"}}
foreach($p in @($Request,(Join-Path $Repository 'schemas/request-v1.json'))){if(!(Test-Path -LiteralPath $p -PathType Leaf)){throw "Missing $p"}}
if(Test-Path -LiteralPath $Output){throw 'Fresh output required'}
New-Item -ItemType Directory -Path $Output|Out-Null
$saved=@{};foreach($n in @('TEMP','TMP','PYTHONPATH')){$saved[$n]=[Environment]::GetEnvironmentVariable($n,'Process')}
try {
 $env:TEMP=$Output;$env:TMP=$Output;$env:PYTHONPATH=$SchemaTools
 $source=Get-Content -LiteralPath $Request -Raw|ConvertFrom-Json -AsHashtable
 $element=@($source.circuit.elements|Where-Object {$_.kind -eq 'junction_storage' -and $_.early_effect})
 if($element.Count -ne 1){throw 'Expected exactly one coupled storage companion'}
 # Other element families have separate schema-qualification work. Keep the
 # complete nodes and exact serialized companion, without claiming solvability.
 $source.circuit.elements=@($element[0]);$source.analysis=@{kind='operating_point'}
 $schema=Join-Path $Repository 'schemas/request-v1.json'
 $positive=Join-Path $Output 'coupled.json';$source|ConvertTo-Json -Depth 50|Set-Content -LiteralPath $positive
 python -m check_jsonschema --version *> (Join-Path $Output 'version.log')
 if($LASTEXITCODE){throw 'Validator unavailable'}
 python -m check_jsonschema --schemafile $schema $positive *> (Join-Path $Output 'positive.log')
 if($LASTEXITCODE){throw 'Typed coupled companion failed schema'}
 $faults=@('missing_control','negative_node','zero_early','unknown_control_field','negative_transit','unknown_model_field')
 foreach($fault in $faults){
  $bad=Get-Content -LiteralPath $positive -Raw|ConvertFrom-Json -AsHashtable;$e=$bad.circuit.elements[0]
  switch($fault){
   'missing_control' {$e.early_effect.Remove('cp')|Out-Null}
   'negative_node' {$e.early_effect.cp=-1}
   'zero_early' {$e.early_effect.volts=0}
   'unknown_control_field' {$e.early_effect['ignored']=1}
   'negative_transit' {$e.model.transit_seconds=-1}
   'unknown_model_field' {$e.model['ignored']=1}
  }
  $path=Join-Path $Output ($fault+'.json');$bad|ConvertTo-Json -Depth 50|Set-Content -LiteralPath $path
  python -m check_jsonschema --schemafile $schema $path *> (Join-Path $Output ($fault+'.log'))
  if($LASTEXITCODE -ne 1){throw "Malformed $fault not rejected"}
 }
 @{positive=1;negative=$faults.Count;source_request_sha256=(Get-FileHash -LiteralPath $Request).Hash;schema_sha256=(Get-FileHash -LiteralPath $schema).Hash;scope='Exact coupled companion shape, extracted from typed request; no circuit solvability claim'}|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $Output 'summary.json')
}finally{foreach($n in $saved.Keys){[Environment]::SetEnvironmentVariable($n,$saved[$n],'Process')}}
