# October 30 soak follow-up

Base: fd34c53db0ab119da0ee8872fb3c9ae6d6643802 plus the exact source hashes in
[soak-20261030-source-hashes.json](soak-20261030-source-hashes.json). This is a
finite follow-up, not another completed six-hour campaign or M1 qualification.

## Fixes and regressions

- CSC union: an isolated, killable child checks the minimal cross-column witness
  and 256 canonical-pattern properties (seed 2026103001). The three-second child
  deadline catches the hanging `<` to `<=` mutation before the framework timeout.
  The existing numerical implementation is unchanged; the correction is test coverage.
- Exact mutation selectors now validate only the filename as a path. Rust rest
  patterns containing `..` remain valid description data. Traversal, backslashes,
  controls and oversized selections are rejected. Native inventory equality is
  still enforced. The first tail replay was blocked by this tooling bug, not by
  a numerical failure.
- Independent waveform contracts cover non-unit pulse fall time, offset/amplitude,
  damping, exponential rise-delay finiteness and PWL interpolation. The previously
  unattempted 22-name tail now has 17 caught and five unviable mutations, with no
  survivors, timeouts or unattempted entries in that selected replay.
- Managed JSON guard holders are initialized before compound conditions, avoiding
  Stryker's broad definite-assignment rollback without changing the wire contract.
  A numeric status negative test detects a previously surviving condition mutation.
- Framework pipe draining now shares the execution cancellation budget. Known
  worker executable images are reaped only beneath the unique run directory.
  Windows/Linux self-tests exercise an inherited pipe after parent exit and a
  copied owned worker. This is not arbitrary descendant containment or a sandbox.

## Native mutation evidence

[Selected Rust outcomes](soak-20261030-rust-replays.json) retain original names,
source identity, exact report/log hashes and test attribution. Final CSC run:
`work/framework-mutations/20261030T122057Z-0e171bf8` (one caught). Final tail run:
`work/framework-mutations/20261030T121621Z-b83c37b2` (17 caught, five unviable).
The four conditionally equivalent October 29/30 survivors remain retained; these
replays neither reclassify them nor prove general equivalence.

[Managed replay records](soak-20261030-managed-replays.json) preserve both runs:

| Run | Generated | Killed | Survived | Timeout | Compile error | Uncovered | Ignored |
|---|---:|---:|---:|---:|---:|---:|---:|
| 20261030T121902Z-4dd2e562 | 200 | 164 | 9 | 4 | 10 | 1 | 12 |
| 20261030T124250Z-c9f5945d | 201 | 169 | 10 | 6 | 2 | 1 | 13 |

The first native report completed, but two verified run-owned fixture processes
held inherited pipes open. They were manually stopped before its wrapper finished.
That discovery motivated the supervisor fix. The final run completed in 457.381
seconds with exit zero and automatically reaped two owned workers; no manual
intervention was needed. These are different source populations, not comparable
mutation scores. Timeouts and compile errors are not kills.

Remaining managed work is explicit: missing/wrong-type error code/message guards
(mutants 148/151), cleanup/lifecycle assertions (180/181), diagnostic message
contracts (87/115/128/169), CreateNoWindow policy (93), and conditional decode-only
BOM equivalence (195). Mutant 94 is uncovered. Six stream/cleanup mutations timed
out, including EOF mutant 183 in this full suite; historical isolated catches do
not override this outcome. Pending-loop mutations 99/100 remain compile errors.
Mutant numbers refer only to the final native report, not historical numbering.

## Executed verification

- Windows GNU and Linux Rust: formatting, Windows clippy with warnings denied,
  and all-target regression suites; 402 passed, zero failed, one ignored per
  platform. Linux coverage additionally ran zero doctests. Main Proptest suites
  used 1024 cases per property, 16 methods, seed 2026103002; the separate CSC child
  ran 256 cases at seed 2026103001. This is property testing, not coverage-guided fuzzing.
- Managed client: 72 passed, zero failed/skipped on Windows and Linux. Linux
  isolated snapshot source hashes for Client, TransportContracts and the framework
  helper match this checkpoint. SDKs: Windows 10.0.403, Linux 10.0.402.
- Six Pester 3.4 checks passed, zero failed/skipped (79.558 seconds), including
  selector validation and inherited-pipe cleanup. Both Linux process self-tests
  passed too. The final full Stryker run exercised the current helper/client.
- Real StrictDoc 0.30.4: 14 documents, 267 unique IDs, 254 resolved parents.
  Sphinx 9.1.0 engineering HTML built, including generated public API documentation.
  New requirement IDs remain Draft: MUT-PROGRESS-001, QUAL-EDGE-003, MAN-003,
  MUT-PROCESS-001. Finite checks do not close the wider release gates.

Measured scopes:

| Scope | Lines | Branches | Limitation |
|---|---:|---:|---|
| Owned Rust source incl. in-source tests | 5476/5865 (93.37%) | unavailable | LLVM reported 87 mismatched functions; incomplete mapping |
| Managed client | 171/179 (95.53%) | 111/112 (99.11%) | Windows Coverlet only; excludes native engine and vendor internals |
| Framework helper, scoped Pester execution | 178/255 (69.80%) | 213/354 (60.17%) | per-line maxima for duplicate branch maps; not complete path coverage |

Coverage detail: [Rust](soak-20261030-rust-coverage.json),
[client](managed-coverlet.json), [helper](soak-20261030-tooling-coverage.json).
Native branches, PowerShell coverage, unexecuted failure/platform paths and
vendor coverage remain unavailable. No inferred coverage gain or universal
correctness claim follows from these percentages. Rust/LLVM 1.99.1/23.1.1,
cargo-mutants 27.1.0, Stryker.NET 5.0.0, Coverlet 10.1.0 and dotnet-coverage 18.12.

## Reproduction and artifacts

Use the checked-in host setup and mutation instructions in
[mutation-frameworks.md](../docs/mutation-frameworks.md); preserve the GNU toolchain
pin when reproducing the Windows native results. From an existing checked repo:

```powershell
if (-not (Test-Path -LiteralPath . -PathType Container)) { throw 'Missing checkout' }
if (-not (Test-Path -LiteralPath scripts -PathType Container)) { throw 'Wrong checkout' }
[Environment]::CurrentDirectory = (Get-Location).Path
cargo test --test csc_union_progress
cargo test --test soak_tail_contracts
dotnet run --file scripts/FrameworkMutations.cs -- --selection-self-test
dotnet run --file scripts/FrameworkMutations.cs -- --pipe-self-test
dotnet run --file scripts/FrameworkMutations.cs -- --tool rust --minutes 10 --workers 1 --rust-file src/sparse.rs --rust-test csc_union_progress --rust-names-file evidence/soak-20261030-csc-selection.json
dotnet run --file scripts/FrameworkMutations.cs -- --tool rust --minutes 20 --workers 1 --rust-file 'src/[vw]*.rs' --rust-test soak_tail_contracts,variation --rust-names-file evidence/soak-20261030-tail-selection.json
dotnet run --file scripts/FrameworkMutations.cs -- --tool dotnet --minutes 25 --workers 1
```

Raw local artifacts: `work/soak-fixes-windows.log`,
`work/soak-fixes-linux-coverage.log`, `work/soak-fixes-managed-final-linux.log`,
`work/soak-tooling-final.log`, and the run directories above. The earlier six-hour
run `20261030T050049Z-aba3c2bd` completed 21599.322 seconds, 32,222 batches and
65,990,656 requested Proptest cases, seed 1290648233619383759 (last batch seed
12622358070867488207). That is historical fd34c53 evidence, not this checkpoint.
Raw artifacts remain local. No new soak, CI dispatch or push was performed.
