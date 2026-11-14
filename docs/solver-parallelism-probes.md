# Solver parallelism probes

## Routine Windows verification — November 14, 2026

`scripts/Test.ps1` now runs `scripts/Run-ParallelProbe.ps1 -Probe Intel`
for numerical verification on Windows. Syntax-only tests remain hardware
independent. Individual numerical checkpoints must run this same command even
when using narrower circuit tests instead of the full entry point. Failures are
visible, including missing Intel FP64 support; there is no CPU fallback pass.
SDK 11's file-app output directory is supported alongside the SDK 10 layout.

Actual replay on Intel UHD Graphics 630, driver 31.0.101.2141, OpenCL 3.0 NEO,
.NET 11.0.0: six fixtures (256/4096/65536 rows, batches 1/16), seed 18117,
nine timed repetitions plus initial readback each. All 11,880,960 checked GPU
values agreed with the independent stencil reference; maximum absolute error 0.
Raw report: E:/Artifacts/Spice-parallel-Intel-32140c6a25eb486daac97e0fc37ac7df/intel-opencl.json.
The comparison self-test rejected corrupted values, NaN and wrong shape.
This is sparse matrix-vector multiplication, not factorization, nonlinear
convergence or an integrated GPU deck solver. No GPU kernel line/branch coverage
was measured. Timings are diagnostic and do not establish a general speedup.
Launcher regression verification: Pester 5.7.1, six passed/zero failed/skipped;
51/67 analyzed PowerShell commands covered (76.12%). This is command coverage,
not C# or OpenCL line/branch coverage. Real StrictDoc parsing passed for the
changed development-standards.sdoc. The complete numerical test entry point
was not rerun for this orchestration-only change; the physical GPU command ran.

This checkpoint follows DEV-SOLVER-PROBE-001. It evaluates three independently
runnable paths. The production solver, netlist execution policies and package
dependencies are unchanged.

## Build targets

Run from the existing Spice checkout with GNU Make, PowerShell 7 and an
existing writable CODEX_ARTIFACT_ROOT. On Windows the probes default to
E:/Artifacts when that variable is unset. The hardware-independent aggregate
also needs Rust, .NET 10 and Pester 5.

| Target | Operation |
|---|---|
| `make -j3 parallel-test` | CPU unit tests, Intel validation self-test and launcher Pester tests |
| `make parallel-cpu` | CPU unit tests followed by the threaded sparse LU benchmark |
| `make parallel-intel` | Intel validation self-test followed by the FP64 OpenCL probe |
| `make parallel-cuda-preflight` | CUDA hardware and toolchain prerequisites, with no download or build |
| `make parallel-cuda` | Pinned cuDSS smoke test using CUDSS_CONFIG_DIR and CUDSS_RUNTIME_DIR |

Run the timed targets individually to avoid contention. For a host without
Make, invoke scripts/Run-ParallelProbe.ps1 with -Probe Cpu or -Probe Intel
and add -TestOnly to omit timing and hardware execution. Each CPU and Intel
invocation prints its unique report directory. Builds use an incremental cache per checkout and probe. A lock prevents two invocations from using the same cache concurrently. GNU Make reports a failed
recipe when the underlying probe fails or the CUDA prerequisites are missing.
The CUDA script itself returns 2 for unavailable prerequisites.

The target checkpoint used GNU Make 4.4.1, PowerShell 7.6.5 and Pester 5.7.1
on Windows. The CPU benchmark target completed, and the aggregate target passed three Rust tests, the Intel self-test and seven Pester tests. The aggregate reused the CPU build cache. The Intel target produced a fresh FP64 report with zero maximum
absolute error for all six workloads. Both CUDA targets reported unavailable.
Pester measured 49 of 64 launcher commands exercised (76.56%). Line and
branch metrics were unavailable for the PowerShell launcher. Removing the CPU
test-only guard in a disposable snapshot was caught. The lock test holds the
cache lock and checks that no extra Cargo invocation occurs.
StrictDoc parsed 52 documents with 432 unique IDs and 400 resolved parents.
The target verification artifacts are under E:/Artifacts/Spice-parallel-targets-5fb53a3.

## CPU sparse LU

The standalone Rust probe pins the existing Faer version (0.24.4) with its Rayon
feature and Rayon 1.11.0. It builds grounded square resistor meshes with an ideal
voltage branch. The resulting real MNA matrices are indefinite. Each workload
uses one sparsity pattern and 16 distinct numerical systems. A prescribed
solution generates each right-hand side. The verification gate checks finiteness,
maximum solution error (1e-10), and maximum componentwise backward error (1e-12).

For each size the probe measures symbolic analysis, factorization with a reused
symbolic object, a single right-hand-side solve, and scheduling 16 independent
factor-and-solve jobs. It reports seven-run medians after warming the numeric
paths. Each configuration has a bounded Rayon pool. Batch mode uses serial
Faer inside each job. Timers exclude input generation, verification and pool
creation. These are in-process synthetic solver timings. The current simulator
provider limits its wire interface to 256 unknowns.

For a real circuit, the sparse pattern is the dependency graph. Strongly
connected components can expose block-triangular structure, and symbolic
ordering can expose parallel work inside a large block. The probe reuses Faer's
symbolic analysis. It does not add a custom scheduler or measure graph
partitioning alternatives. Runtime pivoting and coupled circuit structure
can limit within-solve parallelism. Transient time steps and Newton updates
remain ordered, while separate jobs can be scheduled independently.

Run from an existing Spice checkout with Rust 1.99.1 and a working linker.
Use a fresh artifact subdirectory per run.

```powershell
$checkout = (Get-Location).Path
$probe = Join-Path $checkout 'evaluation/cpu-sparse-probe'
$root = $env:CODEX_ARTIFACT_ROOT
if (!$root -and $IsWindows) { $root = 'E:/Artifacts' }
if (!(Test-Path -LiteralPath $probe -PathType Container) -or
    !$root -or !(Test-Path -LiteralPath $root -PathType Container)) {
    throw 'Use an existing Spice checkout and writable artifact root.'
}
$out = Join-Path $root ('Spice-cpu-sparse-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $out | Out-Null
$manifest = Join-Path $probe 'Cargo.toml'
cargo test --locked --release --jobs 4 --target-dir "$out/target" --manifest-path $manifest
if ($LASTEXITCODE) { throw 'CPU probe tests failed.' }
cargo run --locked --release --jobs 4 --target-dir "$out/target" --manifest-path $manifest |
    Tee-Object -FilePath "$out/cpu.csv"
if ($LASTEXITCODE) { throw 'CPU probe failed.' }
```

### Windows results

Intel i5-10505 with six cores and 12 logical processors, Rust 1.99.1,
Windows MSVC target, Cargo Release defaults. Batch timings are milliseconds.

| Unknowns | Nonzeros | Symbolic µs | Factor µs at 1 thread | Best factor µs (threads) | Batch 1 worker | Batch 6 workers | Speedup |
|---:|---:|---:|---:|---:|---:|---:|---:|
| 65 | 290 | 51.9 | 40.3 | 34.6 (2) | 0.681 | 0.203 | 3.35× |
| 257 | 1218 | 262.9 | 205.4 | 205.4 (1) | 4.268 | 1.543 | 2.77× |
| 1025 | 4994 | 949.5 | 3463.6 | 1833.4 (6) | 43.975 | 10.925 | 4.03× |
| 4097 | 20226 | 2066.7 | 20386.9 | 16183.2 (4) | 322.116 | 96.176 | 3.35× |

Single-factor threading helped at 1025 unknowns, but six threads were slower
than one at 4097. A single solve took 2.2 to 528.2 µs at one thread. The maximum
known-solution error was 5.11e-15 and the maximum componentwise backward error
was 6.11e-15. The evidence supports testing bounded batch scheduling against
the actual AC and sweep entry points before selecting any in-solve default.

The three Rust tests passed, covering known solutions, an incorrect finite
result and a NaN result. A deliberate sign reversal in RHS assembly was caught by the known-solution test even though the backward error of that altered system was zero. Combined LLVM coverage from the instrumented probe
and tests was 100% line (155 lines) and 99.39% region (329 regions).
Branch coverage was unavailable with the instrumentation used. Faer, Rayon
and the simulator itself were outside this coverage scope.

The measured source was base eee4c8a plus the standalone probe. Its main.rs SHA-256 is cc602789067136fc8f3c35cac23b0cde78b21d6bc3ed30a9cdb8a6d4cdee43b5.
The raw CPU CSV under E:/Artifacts/Spice-cpu-sparse-probe-eee4c8a has SHA-256
1a3aea829137cd89fff39fe758f7e79e08088c04e8375f3e75508e5041b79c18.

## Intel GPU

The portable probe uses OpenCL through the OS driver with FP64 CSR
matrix-vector multiplication. It uses the selected device's native compute
runtime and checks results on the host. This isolates GPU arithmetic,
dispatch and transfer behavior before choosing a sparse solver library.
Native wgpu Vulkan/SPIR-V is another compute interface, while Ginkgo/SYCL
is a candidate library for a complete sparse solve. The exact Intel Windows
runtime, supported algorithms, precision and convergence still need a
separate Ginkgo experiment. There is no production OpenCL dependency.

See the [Intel probe instructions](../evaluation/IntelGpuProbe/README.md).

### Intel UHD 630 results

The Intel 31.0.101.2140 driver exposed an OpenCL 3.0 GPU with FP64.
The table shows nine-run steady-state medians. The C# CPU baseline is a
single sequential CSR loop. GPU round trip includes input upload, dispatch,
and blocking output readback. It reuses uploaded matrix buffers.

| Rows | Right-hand sides | CPU ms | GPU round trip ms |
|---:|---:|---:|---:|
| 256 | 1 | 0.0019 | 0.1184 |
| 256 | 16 | 0.0277 | 0.1281 |
| 4096 | 1 | 0.0292 | 0.1349 |
| 4096 | 16 | 0.4767 | 0.4078 |
| 65536 | 1 | 0.7232 | 0.5682 |
| 65536 | 16 | 11.7955 | 5.4649 |

Host/context/program setup took 1070.28 ms. The largest case took
2.16 ms for matrix buffer allocation/upload and 9.74 ms for the first
input/dispatch/readback. The steady largest batch was 2.16× faster than
the scalar CPU loop. The 256-row case was slower on the GPU. These
numbers do not compare GPU execution to the threaded sparse LU probe.

All 11,880,960 checked GPU values matched the independent stencil
reference exactly using seed 18117 and .NET SDK 10.0.403. A sign-inversion kernel mutant was
caught at index 0. Coverlet measured 96.44% managed line and 82.05%
managed branch coverage across the self-test and GPU run. The OpenCL
kernel and driver were uninstrumented. The isolated result JSON at
E:/Artifacts/Spice-IntelOpenCL-98865c0a/intel-opencl-isolated.json has SHA-256
9716ffa3e4f8e0303a3cfa29ace79746b2b711e4cea1ab322387a0ec924180a9.

## NVIDIA workstation

[Run the pinned cuDSS workstation probe](cudss-workstation-probe.md) on a
CUDA-capable machine. The official FP64 SPD example establishes basic
toolchain and runtime availability. A general real/complex MNA matrix,
refactorization, numerical parity and transfer-inclusive timings are
separate acceptance gates. The Intel probe and CUDA probe are independent.

## Scope of verification

StrictDoc 0.30.4 parsed 52 documents, 431 unique IDs and 399 resolved
parents. The finite generated workloads and deliberate mutations exercise
the numerical and prerequisite checks. No standalone fuzz campaign, Linux
run, complete GPU circuit simulation or peak memory benchmark was performed.

## References

- [Faer 0.24.4 sparse solvers](https://docs.rs/faer/0.24.4/faer/sparse/linalg/solvers/index.html)
- [Ginkgo build guide](https://github.com/ginkgo-project/ginkgo/blob/develop/INSTALL.md)
- [NVIDIA cuDSS documentation](https://docs.nvidia.com/cuda/cudss/)
