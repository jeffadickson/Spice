# Windows framework mutation process paths

Issue [30](https://github.com/jeffadickson/Spice/issues/30) blocked the historical
overnight Stryker 5.0.0 baseline before any mutants ran. MSBuild produced the
fixture, but launching `Simulator.TransportFixture.exe` through the long C:
checkout/work junction alias raised `Win32Exception` (filename or extension too
long). Bulk bytes being on E: did not shorten the path supplied to Windows.

`FrameworkMutations.cs` now chooses a physical artifact directory before building
its isolated managed snapshot. `SPICE_FRAMEWORK_WORK_ROOT` overrides the root;
otherwise it uses `CODEX_ARTIFACT_ROOT/framework-mutations`, or
`E:\Artifacts\framework-mutations` on this Windows host. The existing parent must
exist. Each campaign still creates a fresh timestamp/GUID child. Source-scoped
Rust/.NET locks retain their existing identity and order. Snapshot input hashes,
copied managed source hashes and logical/physical locations remain explicit.

The verified-soak launcher creates `fm` in its fresh artifact directory, resolves
every ancestor junction and exports that physical path only to its owned child.
It records the path in its launch manifest. It preserves source pinning, fresh
output refusal, canonical worker locks and failure cleanup.

The resolver uses .NET 10's existing
[ResolveLinkTarget](https://learn.microsoft.com/en-us/dotnet/api/system.io.filesysteminfo.resolvelinktarget?view=net-10.0)
for junctions and symbolic links, applied to every component because resolving
only a non-link leaf misses ancestor junctions. No additional runtime dependency
is needed. Windows roots projecting a fixture executable of 240 or more characters
are rejected with an actionable shorter-root error. The policy leaves a margin
below legacy process path boundaries; Unix does not receive this Windows cap.

Before Stryker starts, a real copied test build and an actual fixture process
exchange a bounded request/response. Managed build success alone is insufficient.
Missing fixtures, nonzero exits, incorrect output and deadline failures propagate.
Owned children are reaped on exceptional exit.

Reproduce from PowerShell 7 with the checkout and an existing E: artifact parent:

```powershell
$root = $env:CODEX_ARTIFACT_ROOT
if (-not $root) { $root = 'E:\Artifacts' }
if (-not (Test-Path -LiteralPath $root -PathType Container)) { throw 'Artifact root missing' }
$env:SPICE_PATH_TEST_ROOT = $root
Invoke-Pester -Path tests/FrameworkPhysicalPaths.Tests.ps1 -Output Detailed
```

These tests retain unique bulk fixture outputs in that directory and remove only
their owned NTFS aliases. Their overlong alias reproduces the actual Windows
process failure, then launches the same fixture through the resolved short E:
path. Coverage, exact checks, bounded Stryker counts and limitations are recorded
in [the evidence](../evidence/framework-physical-paths.json). This fix does not
turn the earlier seven-hour campaign into a pass or change property failure policy.
