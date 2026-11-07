# Intel FP64 sparse compute probe

This Windows evaluation uses the installed Khronos OpenCL ICD and Intel GPU
driver. It requires an Intel GPU exposing cl_khr_fp64. It compiles one OpenCL
C kernel through the driver. No NuGet or product runtime dependency is added.
The [.NET 10](https://dotnet.microsoft.com/en-us/download/dotnet/10.0) host is
built in Release mode with the file-based application toolchain. The evaluation
uses JIT execution and records the runtime version. [OpenCL API specification](https://registry.khronos.org/OpenCL/specs/unified/html/OpenCL_API.html)

From the repository root in PowerShell:

```powershell
$probe = 'evaluation/IntelGpuProbe/IntelSpmv.cs'
$root = if ($env:CODEX_ARTIFACT_ROOT) { $env:CODEX_ARTIFACT_ROOT } else { 'E:/Artifacts' }
if (!(Test-Path -LiteralPath $probe -PathType Leaf)) { throw 'Run from the Spice repository root' }
if (!(Test-Path -LiteralPath $root -PathType Container)) { throw 'Create or configure an artifact root first' }
$out = Join-Path $root ('Spice-IntelOpenCL-' + [guid]::NewGuid().ToString('N').Substring(0,8))
if (Test-Path -LiteralPath $out) { throw 'Artifact directory already exists' }
New-Item -ItemType Directory -Path $out | Out-Null
dotnet build $probe -c Release --artifacts-path (Join-Path $out 'build')
if ($LASTEXITCODE -ne 0) { throw 'Probe build failed' }
$app = Join-Path $out 'build/bin/release/IntelSpmv.dll'
if (!(Test-Path -LiteralPath $app -PathType Leaf)) { throw 'Probe binary missing' }
$priorTiering = $env:DOTNET_TieredCompilation
try {
    $env:DOTNET_TieredCompilation = '0'
    dotnet $app --self-test
    if ($LASTEXITCODE -ne 0) { throw 'Probe self-test failed' }
    dotnet $app | Tee-Object -FilePath (Join-Path $out 'intel-opencl.json')
    if ($LASTEXITCODE -ne 0) { throw 'GPU run failed' }
} finally {
    $env:DOTNET_TieredCompilation = $priorTiering
}
```

The synthetic CSR stencil has five entries per row. The probe measures 256,
4,096 and 65,536 rows with batches of one and sixteen right-hand sides.
GPU timing includes writing the input vector, executing the kernel and
blocking on output readback. Matrix upload and process-level driver/kernel
setup are reported separately. The CPU comparator uses the same CSR data and
a single sequential C# loop. Run without competing benchmarks or builds when
comparing timings. The independent reference computes the stencil directly
rather than indexing the CSR structure. Seed 18117 and changing right-hand
sides exercise FP64 precision and detect stale output. The self-test checks
shape, corrupted values and nonfinite results.

This exercises FP64 matrix-vector products only. It does not time LU,
triangular solves, Newton iterations, or complete simulations. The synthetic
stencil and scalar CPU loop characterize this probe and cannot establish a
break-even point for the production sparse provider.
