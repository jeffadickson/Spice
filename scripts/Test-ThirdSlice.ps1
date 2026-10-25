# Run the focused variation/sampling contract suite from the repository root.
# The full checkpoint remains the release gate; this script is for quick local
# iteration and intentionally does not claim any coverage percentage.
$ErrorActionPreference = 'Stop'
cargo test --locked --test variation
if ($LASTEXITCODE -ne 0) { throw 'Variation tests failed' }
cargo test --locked --test sampling
if ($LASTEXITCODE -ne 0) { throw 'Sampling tests failed' }
dotnet build dotnet/Simulator.Conformance/Simulator.Conformance.csproj
if ($LASTEXITCODE -ne 0) { throw 'Managed contract build failed' }
