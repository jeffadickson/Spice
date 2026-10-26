# Focused nonlinear diode/Newton contract gate. Full checkpoints remain required.
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
if (-not (Test-Path -LiteralPath $root -PathType Container)) { throw 'Checkout directory unavailable.' }
Push-Location -LiteralPath $root
try {
# Include recovery and coupled physical oracles as well as device equations.
cargo test --locked --lib --test nonlinear --test continuation --test continuation_hardening --test continuation_networks
if ($LASTEXITCODE -ne 0) { throw 'Nonlinear Rust contracts failed' }
dotnet build dotnet/Simulator.Conformance/Simulator.Conformance.csproj
if ($LASTEXITCODE -ne 0) { throw 'Managed nonlinear DTO contract build failed' }
} finally { Pop-Location }
