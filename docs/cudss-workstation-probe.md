# NVIDIA cuDSS workstation probe

The optional probe exercises an FP64 sparse direct solve on an NVIDIA CUDA
device. It uses NVIDIA's Apache-2.0 sample from a pinned commit, verifies both
downloaded source hashes, and leaves the production simulator independent of
CUDA. The upstream sample solves a small symmetric positive-definite system
and checks the solution against its known answer. This is a device and toolchain
smoke test. General unsymmetric modified nodal analysis, complex AC solves,
refactorization, and performance remain separate qualification steps.

Use PowerShell 7, a working NVIDIA driver with `nvidia-smi`, a compatible
CUDA toolkit with `nvcc`, CMake 3.24 or later, an NVIDIA-supported host
compiler, and cuDSS 1.0.0 or later compatible with that toolkit. On Windows,
open PowerShell from a Developer PowerShell environment if the host toolchain
requires it. The `cudss_DIR` argument points to the directory containing the
installed cuDSS CMake package config. The runtime argument points to the
directory containing its DLL or shared library. On a Linux workstation use
a writable native filesystem for artifacts. On Windows the script uses
`CODEX_ARTIFACT_ROOT` or an existing `E:/Artifacts` unless overridden.

```powershell
$checkout = (Get-Location).Path
$script = Join-Path $checkout 'scripts/Run-CuDssProbe.ps1'
if (!(Test-Path -LiteralPath $checkout -PathType Container) -or
    !(Test-Path -LiteralPath $script -PathType Leaf)) { throw 'Run from the Spice checkout.' }
foreach ($dir in $env:CUDSS_CONFIG_DIR, $env:CUDSS_RUNTIME_DIR, $env:CODEX_ARTIFACT_ROOT) {
    if (!$dir -or !(Test-Path -LiteralPath $dir -PathType Container)) {
        throw 'Set CUDSS_CONFIG_DIR, CUDSS_RUNTIME_DIR and CODEX_ARTIFACT_ROOT to existing directories.'
    }
}
& $script -PreflightOnly
if ($LASTEXITCODE -ne 0) { throw "cuDSS preflight returned $LASTEXITCODE" }
& $script
if ($LASTEXITCODE -ne 0) { throw "cuDSS smoke returned $LASTEXITCODE" }
```

Preflight reports tool and driver availability without downloading anything.
The smoke test downloads the pinned source files, builds the vendor Release
target for the detected CUDA architecture, and runs the sample. The fresh
artifact directory contains the source, executable, and `report.json` with
GPU enumeration, driver and compiler evidence, source and binary hashes,
command logs, and the sample's numerical pass marker and solution values. Child commands have
bounded run times. Exit 2 means unavailable, and exit 1 means failed. A
successful preflight alone does not establish a successful solve.

The Intel-only development machine exercised the unavailable-prerequisite
path. Pester 5.7.1 passed both host-boundary tests with 42.86% command coverage
(66 of 154 commands) and no branch measurement. A deliberate change that
reported missing tools as exit 0 was caught. NVIDIA compilation and execution
require the workstation. Smoke-test wall time includes process startup.

- [NVIDIA cuDSS setup documentation](https://docs.nvidia.com/cuda/cudss/getting_started.html)
- [Pinned NVIDIA sample source](https://github.com/NVIDIA/CUDALibrarySamples/tree/8b0573b11d183d9b3a36cd2955aa31c16cc61b95/cuDSS/simple)
