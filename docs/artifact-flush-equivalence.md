# Issue47: unbuffered File flush survivor

The raw cargo-mutants result remains **one survived**, not a killed mutant.
Run `20261114T080050Z-eeffe372` tested source
`0face5844c108bbf80e2af39a2d9ec275c76f703` and reported:

`src/artifact.rs:43:9: replace LimitedFile::flush -> io::Result<()> with Ok(())`.

The wrapper owns an unbuffered `std::fs::File` and forwards `flush` directly.
Rust's [File source documentation](https://doc.rust-lang.org/src/std/fs.rs.html)
states that this operation is currently a no-op on Windows and Unix because
File has no intermediate buffer. Publication separately calls `sync_all` for
both result bytes and the commit marker before their renames. Replacing only
the delegated no-op does not remove either durability request.

Classification: **equivalent on the assessed pinned Windows target**, not a
missing numerical assertion. A fake test purporting to kill it would be wrong.
The delegation stays in production code so a future standard-library/platform
behavior change is preserved. No broad mutation exclusion or skip annotation
was added; future variants and the unchanged raw report remain visible.

## Executable evidence

`scripts/Probe-FileFlush.ps1` compiles `experiments/file-flush.rs` with the selected
checkout's pinned compiler. It independently opens/readbacks four payload sizes
(0, 1, 4096 and 65536 bytes) before flush, after flush and after `sync_all`.

Executed on Windows 10.0.26300, rustc 1.99.1 (58cf8f9c3, 2026-10-13): **four
cases passed**. Raw output:
`E:/Artifacts/Spice-flush-equivalence-1aa258e-20261114/probe-formatted/result.json`.

Probe source SHA-256:
`cad63fe5b363cff93c932e69a9bab892ed8a31680088defcfe07af6cad75583c`.
Probe executable SHA-256:
`134a38435c53d6f7337bb2b289cb936e129dd139ff03e92b370346e0ac32774f`.

The current online standard-library documentation was inspected. The versioned
1.99.1 HTML source was unavailable through the browsing tool; pinned Windows
runtime observations are recorded separately from that current-source citation.
Unix was not executed. These finite readback checks are not a power-loss test,
formal equivalence proof, guarantee about every filesystem or future Rust release.
Reassess if File is replaced with a buffered writer or the underlying contract
changes. No simulator numerical behavior was changed by this classification.

## Reproduce

From the Spice checkout, with an existing writable artifact root:

```powershell
if (!(Test-Path -LiteralPath 'scripts' -PathType Container)) { throw 'Run from Spice checkout' }
$artifactRoot = if ($env:CODEX_ARTIFACT_ROOT) { $env:CODEX_ARTIFACT_ROOT } else { 'E:/Artifacts' }
if (!(Test-Path -LiteralPath $artifactRoot -PathType Container)) { throw 'Missing artifact root' }
./scripts/Probe-FileFlush.ps1 -OutputDirectory (Join-Path $artifactRoot ('Spice-flush-' + [guid]::NewGuid().ToString('N')))
```

Trace: ARTIFACT-MUTANT-CLASSIFICATION-001. Coverage limitations: the new diagnostic
is a four-case standalone I/O experiment, not a new whole-core coverage or mutation
score. Artifact publication regression/coverage evidence is recorded separately.

## Publication regressions and coverage

The real `storage_power` integration binary passed **five tests**, including
publication round trip, collision refusal and corruption detection. The test
was also executed with a caller-relative `WorkingDirectory=relative-run`; its
artifacts appeared under the expected E: subtree after absolute normalization.
Coverage from that five-test scope: `src/artifact.rs` **90/99 lines**; the entire
owned library exercised by this test **1899/8011 lines**. Stable Rust branch
coverage is unavailable. Other tests, executable main and the standalone probe
are outside those line counts; this is not whole-suite coverage.

Pester 5.7.1 passed **four tests**, covering output preservation, missing parent,
missing coverage working directory before compilation, and the pinned probe.
The successful probe uses a unique artifact-root child regardless of Pester's
default scratch location. Instrumented command coverage for the two PowerShell
helpers was **76/228 (33.33%)**; no PowerShell branch percentage is available.
Raw outputs are `artifact-coverage-relative`, `pester-reviewed.xml` and
`pester-reviewed-coverage.xml` under the artifact directory above. The toolchain
coverage environment seed was `88320261103`; these five tests are deterministic
and do not establish randomized/fuzz coverage. No new broad mutation campaign
was launched or reported as passing.

A fresh simulated source review identified the relative-directory resolution
and default Pester-output issues. Both were fixed and checked using actual
relative-path execution and a repeated Pester run. No external expert review or
formal equivalence proof is implied.
