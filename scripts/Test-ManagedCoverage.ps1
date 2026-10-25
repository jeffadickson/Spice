param([string]$CoverageTool = 'dotnet-coverage')
# Run the collector over the real managed contract executable, excluding it from SDK metrics.
$ErrorActionPreference = 'Stop'
$env:DOTNET_COVERAGE_TELEMETRY_OPTOUT = '1'
dotnet build dotnet/Simulator.Conformance/Simulator.Conformance.csproj
if ($LASTEXITCODE -ne 0) { throw 'Managed build failed' }
New-Item -ItemType Directory -Force -Path build/coverage | Out-Null
$exe = Join-Path (Get-Location) ('target/debug/circuit-simulator' + $(if ($IsWindows) { '.exe' } else { '' }))
& $CoverageTool collect -f cobertura -o build/coverage/managed.cobertura.xml dotnet dotnet/Simulator.Conformance/bin/Debug/net10.0/Simulator.Conformance.dll $exe
if ($LASTEXITCODE -ne 0) { throw 'Managed coverage collection failed' }
[xml]$xml = Get-Content -Raw -LiteralPath build/coverage/managed.cobertura.xml
$package = $xml.coverage.packages.package | Where-Object name -eq 'Simulator.Client'
if (-not $package) { throw 'Client coverage package absent' }
# Exclude source-generator output by exact owned-file paths; retain state-machine
# branches mapped to owned source. Union line locations to avoid duplicate classes.
$owned = @(Get-ChildItem dotnet/Simulator.Client -Filter '*.cs' -File | ForEach-Object FullName)
foreach ($class in @($package.classes.class)) {
    if ($class.filename -notin $owned) { [void]$package.classes.RemoveChild($class) }
}
$locations = @{}
foreach ($class in @($package.classes.class)) {
    foreach ($line in @($class.lines.line)) {
        $key = $class.filename + ':' + $line.number
        if (-not $locations.ContainsKey($key) -or [int]$line.hits -gt [int]$locations[$key].hits) { $locations[$key] = $line }
    }
}
$lines = @($locations.Values)
$covered = @($lines | Where-Object { [int]$_.hits -gt 0 }).Count
$branches = 0; $coveredBranches = 0
foreach ($line in $lines) {
    if ($line.'condition-coverage' -match '\((\d+)/(\d+)\)') { $coveredBranches += [int]$Matches[1]; $branches += [int]$Matches[2] }
}
$lineRate = $covered / $lines.Count
$branchRate = $(if ($branches) { $coveredBranches / $branches } else { 0 })
foreach ($other in @($xml.coverage.packages.package)) {
    if ($other -ne $package) { [void]$xml.coverage.packages.RemoveChild($other) }
}
foreach ($node in @($package, $xml.coverage)) {
    $node.SetAttribute('line-rate', $lineRate.ToString([cultureinfo]::InvariantCulture))
    $node.SetAttribute('branch-rate', $branchRate.ToString([cultureinfo]::InvariantCulture))
}
$xml.coverage.SetAttribute('lines-valid', [string]$lines.Count)
$xml.coverage.SetAttribute('lines-covered', [string]$covered)
$xml.coverage.SetAttribute('branches-valid', [string]$branches)
$xml.coverage.SetAttribute('branches-covered', [string]$coveredBranches)
$report = [ordered]@{
    status = 'measured'; tool = (& $CoverageTool --version | Out-String).Trim()
    scope = 'Owned Simulator.Client .cs files only; unique source lines and mapped branches; generated JSON metadata excluded'
    source_sha256 = (Get-FileHash -LiteralPath dotnet/Simulator.Client/Client.cs -Algorithm SHA256).Hash.ToLowerInvariant()
    source_hashes = $( $hashes=@{}; Get-ChildItem -LiteralPath dotnet/Simulator.Client -Filter '*.cs' -File | ForEach-Object { $hashes['dotnet/Simulator.Client/'+$_.Name]=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant() }; $hashes )
    line_percent = 100 * [double]::Parse($package.'line-rate', [cultureinfo]::InvariantCulture)
    branch_percent = 100 * [double]::Parse($package.'branch-rate', [cultureinfo]::InvariantCulture)
    lines = $lines.Count
    covered_lines = $covered
    branches = $branches
    covered_branches = $coveredBranches
    exclusions = @('Rust/native runtime code', 'generated/framework code', 'test executable')
}
$report | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath evidence/managed-coverage.json -Encoding utf8
$xml.Save((Join-Path (Get-Location) 'evidence/managed-coverage.cobertura.xml'))
$report | ConvertTo-Json -Depth 5
