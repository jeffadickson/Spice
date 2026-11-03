# Ten stateless gain transient mismatches

The frozen requested cohort at source `408d718` contains ten stateless gain mismatches: dot_model_ref, io_ordering, long_names, mixed_case and parsing, plus their mirrors. These are five distinct expanded decks. The RC ramp is a separate dynamic case and remains excluded and unresolved here. Original statuses and tolerances are unchanged.

All original native and engine samples agree with an independent closed form on their own actual timestamps. Maximum voltage discrepancies are 3.55e-15 V for the engine and 1.73e-14 V for ngspice. Root KCL residuals are at most 3.47e-18 A and 1.04e-17 A respectively; direct source and gain constraints also hold. Private native voltages are reconstructed only when metadata proves them constant; unknown values are never replaced by zero.

For each of the 44 mapped root observables, interpolating exact closed-form values on the native grid reproduces the original failed count and maximum error. The error-decomposition residual is at most 1.69e-14 V or 1.74e-17 A. Interpolation is therefore the common cause in this finite cohort. This does not establish a general waveform-accuracy bound or diagnose the RC ramp.

The original grids contain 101 engine and 108 native samples, sharing only the two endpoints exactly. Generated metadata gives amplitude 1 V and frequency 1 kHz for four profiles; long_names uses amplitude 1 V and frequency 2 kHz. The latter's maximum output interpolation error is 0.01589644 V; the 1 kHz output error is 0.003978907 V. Bounds remain the original 1e-6 V / 1e-9 A absolute plus 1e-4 relative.

A diagnostic POC adds one zero-amp current source across existing nodes, with zero-valued PWL points at the native timestamps. It preserves original circuit elements, physical source waveforms, unknown count, method, TMAX, stop and max_steps. The existing scheduler emits 207 engine samples. Of 108 native timestamps, 103 match bit-for-bit and all mapped values pass there; five nearest times differ by one ULP (1.0842e-19 s), which are explicitly not counted as exact matches. There are 104 extra engine timestamps. Every POC sample independently matches the closed form, but full engine-grid comparison against interpolated original RAW still has the original failures. A neutral breakpoint source alone does not resolve the reporting comparison.

Recommended direction for #28: define an explicit common reporting-coordinate/oracle contract and qualify floating-point coordinate roundtrips. Retain actual solver knots, check all requested report coordinates, and expose any interpolation error budget separately from physical/numerical disagreement. The zero-current POC should remain a diagnostic; it neither qualifies all engine knots against native data nor changes the original result classification. No product, parser, solver or library migration is proposed by this helper.

The standalone .NET 10 helper references already compiled, frozen metadata DLLs, so it rebuilds no product project and adds no production dependency. It uses generated `DeckParser`/`RequestedAnalysis` and existing `AsciiRaw`; there is no ad-hoc deck parser or numerical MNA solve. Source/gain constraints produce affine a*u(t)+b expressions; root resistor and observed source currents provide independent KCL checks. Seven deterministic contracts include extrapolation/dynamic/conflicting-constraint refusals and deliberate 1 mV observation-fault sensitivity. No new source mutation or broad fuzz campaign ran.

Coverlet 10.1.0 measured only the helper: 117/117 mapped lines and 210/266 mapped branches (78.95%). The first attempt without `--include-test-assembly` emitted an empty map and is not coverage evidence. These compact executable maps exclude reused frontend/RAW libraries and native code; uncovered malformed-input branches remain open. The helper is managed, not NativeAOT.

Reproduction (PowerShell; substitute retained paths and a fresh output parent):

```powershell
$repo = Join-Path $env:USERPROFILE 'Documents/Codex/2026-10-24/how/outputs/rust-circuit-simulator/work/semiconductor-profile'
if (!(Test-Path -LiteralPath $repo -PathType Container)) { throw 'Missing checkout' }
Set-Location -LiteralPath $repo
$refs = 'E:/Artifacts/Spice-compatibility-20261103T043926Z/requested-cohort-build/bin/debug'
$cohort = 'E:/Artifacts/Spice-compatibility-20261103T043926Z/requested-cohort-408d718'
$engine = 'E:/Artifacts/Spice-compatibility-20261103T043926Z/fixed-cohort-engine/circuit-simulator.exe'
$artifactRoot = if ($env:CODEX_ARTIFACT_ROOT) { $env:CODEX_ARTIFACT_ROOT } else { 'E:/Artifacts' }
foreach ($directory in @($refs, $cohort, $artifactRoot)) {
    if (!(Test-Path -LiteralPath $directory -PathType Container)) { throw "Missing $directory" }
}
if (!(Test-Path -LiteralPath $engine -PathType Leaf)) { throw 'Missing frozen engine' }
$run = Join-Path $artifactRoot ('Spice-gain-grid-diagnosis-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $run | Out-Null
# The POC accepts only the original cohort engine hash, not a later mutable build.
dotnet run --project tools/GainGridDiagnosis/GainGridDiagnosis.csproj --artifacts-path (Join-Path $run 'build') "-p:QualificationReferenceDir=$refs" -- $cohort (Join-Path $run 'diagnosis') $engine
if ($LASTEXITCODE) { throw 'Diagnosis failed' }
```

Bounded source-controlled summary: `evidence/gain-grid-diagnosis.json`. Full metrics, original-input rechecks, one-ULP coordinates and transformed POC requests/results remain in the unique local artifact directory recorded there. No original artifacts are rewritten.
