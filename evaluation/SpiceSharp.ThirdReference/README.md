# Isolated SpiceSharp numerical reference

This executable is qualification tooling, not a dependency of the simulator.
It pins SpiceSharp3.2.3 and SpiceSharp-Parser3.4.1 (locked transitive
SpiceSharpBehavioral3.2.0). Each bounded job runs one OP, AC or transient and
uses direct public voltage/current exports. Current is positive from the named
element's positive terminal to its negative terminal, in amperes.

From an existing checkout in PowerShell, check the repository and output parent
with `Test-Path -LiteralPath ... -PathType Container` first, then run:

```powershell
# $repo is an existing checkout; $artifacts is a fresh child of an existing E: parent.
if (!(Test-Path -LiteralPath $repo -PathType Container)) { throw 'Missing checkout' }
if (!(Test-Path -LiteralPath (Split-Path $artifacts -Parent) -PathType Container)) { throw 'Missing artifact parent' }
& (Join-Path $repo 'scripts/Setup-SpiceSharpReference.ps1') -OutputDirectory $artifacts
```

The setup returns stock and explicitly patched workers plus a manifest.
The stock Behavioral emitter throws an illegal-short-branch exception on the
PSpice switch expression. Setup verifies pinned MIT source archive and file
preimages, then changes16 short IL branch opcodes to long forms, leaving formulas
unchanged. The manifest records source, license, patch and DLL hashes. The patch
is copied only to its own worker output, never the global package cache or the
production dependency graph. Always pass its manifest when using that worker.
Stock results and patched-reference results must stay distinct.

Worker invocation is `dotnet <worker.dll> <job.json> <fresh-result.json>`.
Job fields: case_id, mode(op/ac/transient), deck_path, probes([{id,kind,target}]),
optional patch_manifest. Result includes explicit status, V/A descriptors,
op_index/Hz/s coordinate, assembly hashes, deck hash, patch hash and samples.
The supervising runners enforce30s child deadlines and finite vector shapes.
Missing, failed or unsupported reference output is never substituted with zero.

See docs/third-reference-results.md for actual execution and upstream gaps.
The independent parser can stall on long blank-line runs; the OP/AC coordinator
retains the raw validated projection and separately hashes a third-parser-only
blank-line normalization. No syntax/model substitutions are made to force parity.
