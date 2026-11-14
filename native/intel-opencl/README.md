# Experimental Intel circuit provider

This is a hybrid GPU-assisted **linear solve** provider for the existing Rust
circuit engine. Assembly, models, Newton iteration, integration, residual audits
and small GMRES host operations remain on the CPU. ViennaCL's FP64 vector/matrix
operations run on an explicitly enumerated Intel GPU. No CPU linear-solver
fallback is allowed. The normal LU/QR/Faer builds remain independent.

Currently assessed platform: Windows x64, Intel UHD Graphics 630,
driver 31.0.101.2141. Linux and other devices are unqualified. General MNA may
fail convergence; dimensions remain bounded to 256 original unknowns. AC uses
an exact real block embedding with at most 512 native unknowns.

## Build and exercise

Reuse a clean ViennaCL checkout at commit
`77e9049d5622d46b6801a725917a931eda096f76` (release-1.7.1).
With the repository and existing artifact root resolved in PowerShell:

```powershell
if (!(Test-Path -LiteralPath $repository -PathType Container)) { throw 'Missing source' }
if (!(Test-Path -LiteralPath $viennaCl -PathType Container)) { throw 'Missing pinned dependency' }
if (!(Test-Path -LiteralPath $artifactRoot -PathType Container)) { throw 'Missing artifact root' }
$native = Join-Path $artifactRoot ('Spice-intel-native-' + [guid]::NewGuid().ToString('N'))
& (Join-Path $repository 'scripts/Build-IntelProvider.ps1') -ViennaClRoot $viennaCl -OutputDirectory $native
if (!$?) { throw 'Provider build failed' }
$env:SPICE_INTEL_HOST = Join-Path $native 'spice_intel_opencl.dll'
& (Join-Path $repository 'scripts/Test-IntelCircuits.ps1') -ArtifactRoot $artifactRoot
```

Configure the repository's process-tree resource guard before memory-intensive
builds/tests. All build, shader-cache, TEMP/TMP and test paths must remain on E:.
Use `cargo ... --features intel-opencl` and backend `intel` for the optional
provider; `--intel-device` enumerates metadata without starting a compute context.
The standalone/schematic UI is not qualified for this provider by these tests.

`Build-IntelEngine.ps1` is a diagnostic snapshot path: Cargo builds the library
graph, then rustc links the normal CLI source into a fresh executable. It avoids
overwriting an executable used by another job. It is a dev-profile executable,
not evidence of an optimized release build. `Test-IntelSnapshot.ps1` records
library and CLI hashes separately and rejects changing dependencies.

## Ownership and lifecycle

The trusted native module path is process configuration, never deck-controlled.
Only the isolated host crate uses unsafe FFI. The context is serialized and
explicitly shut down before DLL process detach. Shutdown is a **final process
operation**; reopening the provider afterward is not qualified. The CLI and
physical integration test each shut down once, after all solves.

CMake produces a narrow, noticed overlay of ViennaCL's `ocl/backend.hpp` that
adds context erasure; assigning an empty context is invalid in this version.
No numerical dependency implementation is changed. The dependency checkout
must be clean, including untracked headers. Build manifests retain all header
hashes and the generated overlay hash.

## Attribution

| Material | Author/source | License and redistribution |
|---|---|---|
| ViennaCL 1.7.1 numerical and OpenCL headers | ViennaCL contributors, [pinned source](https://github.com/viennacl/viennacl-dev/tree/77e9049d5622d46b6801a725917a931eda096f76) | MIT/X11; build copies `LICENSE` to `VIENNACL-LICENSE.txt`; retain with DLL |
| Generated context-lifecycle overlay | Original ViennaCL header plus owned accessor described above | Retains upstream notices; separately hashed modification |
| OpenCL loader/import table | Installed Windows OpenCL ICD loader | System loader is not redistributed; only an import library is generated locally |
| C ABI glue, Rust adapter and orchestration | This repository | Repository license |

See `docs/intel-circuit-results.md` for actual deck evidence, failures, measured
latency, build profiles and coverage limitations. Matrix multiplication alone
does not establish circuit execution, accuracy or acceleration.
