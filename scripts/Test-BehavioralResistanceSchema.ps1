param(
    [Parameter(Mandatory)][string]$Repository,
    [Parameter(Mandatory)][string]$Output,
    [Parameter(Mandatory)][string]$SchemaTools,
    [Parameter(Mandatory)][string[]]$Requests
)
# A maintained JSON Schema validator is a tooling-only Python exception. No
# parser/validator implementation or Python dependency enters the product core.
# Qualified tool pins: check-jsonschema 0.39.0, jsonschema 4.26.0. Acquisition
# URLs and immutable wheel/dependency hashes belong in the verification evidence.
$ErrorActionPreference='Stop'
foreach($p in @($Repository,$SchemaTools,(Split-Path -Parent $Output))){
    if(!(Test-Path -LiteralPath $p -PathType Container)){throw "Missing directory: $p"}
}
if(Test-Path -LiteralPath $Output){throw 'Fresh schema result directory required'}
if($Requests.Count -lt 1 -or $Requests.Count -gt 32){throw 'Expected 1..32 bounded request fixtures'}
$schema=Join-Path $Repository 'schemas/request-v1.json'
foreach($p in @($schema)+$Requests){if(!(Test-Path -LiteralPath $p -PathType Leaf)){throw "Missing input: $p"}}
New-Item -ItemType Directory -Path $Output|Out-Null
$saved=@{}
foreach($name in 'TEMP','TMP','PYTHONPATH'){$saved[$name]=[Environment]::GetEnvironmentVariable($name,'Process')}
try {
    $env:TEMP=$Output;$env:TMP=$Output;$env:PYTHONPATH=$SchemaTools
    python -m check_jsonschema --version *> (Join-Path $Output 'version.log')
    if($LASTEXITCODE){throw 'JSON Schema validator unavailable'}
    python -m check_jsonschema --schemafile $schema @Requests *> (Join-Path $Output 'positive.log')
    if($LASTEXITCODE){throw 'Actual typed request failed schema validation'}
    $negative=@('missing_expression','unknown_operation','unknown_field','invalid_node','invalid_constant','null_expression')
    foreach($fault in $negative){
        $request=Get-Content -LiteralPath $Requests[0] -Raw|ConvertFrom-Json -AsHashtable
        $element=$request.circuit.elements|Where-Object {$_.kind -eq 'behavioral_resistor'}|Select-Object -First 1
        if(!$element){throw 'First request must contain the tested resistance model'}
        switch($fault){
            'missing_expression' {$element.Remove('expression')|Out-Null}
            'unknown_operation' {$element.expression=@{op='shell'}}
            'unknown_field' {$element['invented']=42}
            'invalid_node' {$element.cp=-1}
            'invalid_constant' {$element.expression=@{op='constant';value='not-a-number'}}
            'null_expression' {$element.expression=$null}
        }
        $path=Join-Path $Output ($fault+'.json')
        $request|ConvertTo-Json -Depth 80|Set-Content -LiteralPath $path
        python -m check_jsonschema --schemafile $schema $path *> (Join-Path $Output ($fault+'.log'))
        if($LASTEXITCODE -ne 1){throw "Malformed $fault was not rejected as a schema failure"}
    }
    [ordered]@{positive=$Requests.Count;negative=$negative.Count;failed=0;schema_sha256=(Get-FileHash -LiteralPath $schema).Hash.ToLowerInvariant();limitations='Schema does not establish expression budgets, model domains or convergence.'}|
        ConvertTo-Json|Set-Content -LiteralPath (Join-Path $Output 'summary.json')
} finally {
    foreach($name in $saved.Keys){[Environment]::SetEnvironmentVariable($name,$saved[$name],'Process')}
}
exit 0
