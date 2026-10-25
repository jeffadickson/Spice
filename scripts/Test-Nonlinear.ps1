# Focused nonlinear diode/Newton contract gate. Full checkpoints remain required.
$ErrorActionPreference = 'Stop'
cargo test --locked --test nonlinear
if ($LASTEXITCODE -ne 0) { throw 'Nonlinear Rust contracts failed' }
dotnet build dotnet/Simulator.Conformance/Simulator.Conformance.csproj
if ($LASTEXITCODE -ne 0) { throw 'Managed nonlinear DTO contract build failed' }
