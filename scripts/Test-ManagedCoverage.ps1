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
$lines = @($package.SelectNodes('.//class/lines/line'))
$report = [ordered]@{
    status = 'measured'; tool = (& $CoverageTool --version | Out-String).Trim()
    scope = 'Simulator.Client only; conformance executable excluded from SDK metrics'
    source_sha256 = (Get-FileHash -LiteralPath dotnet/Simulator.Client/Client.cs -Algorithm SHA256).Hash.ToLowerInvariant()
    line_percent = 100 * [double]::Parse($package.'line-rate', [cultureinfo]::InvariantCulture)
    branch_percent = 100 * [double]::Parse($package.'branch-rate', [cultureinfo]::InvariantCulture)
    lines = $lines.Count
    covered_lines = @($lines | Where-Object { [int]$_.hits -gt 0 }).Count
    exclusions = @('Rust/native runtime code', 'generated/framework code', 'test executable')
}
$report | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath evidence/managed-coverage.json -Encoding utf8
Copy-Item -LiteralPath build/coverage/managed.cobertura.xml -Destination evidence/managed-coverage.cobertura.xml
$report | ConvertTo-Json -Depth 5
