# Managed mutation follow-up — October 30, 2026

The original 19 unresolved outcomes now have individual native-source dispositions
in [managed-20261030-triage.json](managed-20261030-triage.json). Eight of the ten
original survivors are caught across the Windows and focused Linux replays. Three
of the six original timeouts now produce test-reported kills. The original two
compiler-error candidates were replaced by a different source form, not relabeled
killed. The defensive startup-null diagnostic remains uncovered.

## Changes

- Six independent malformed error-code/message cases challenge type, null, empty
  and whitespace validation. Unsupported-version/envelope errors must carry useful
  nonblank diagnostics; request-size errors identify the limit. Existing client
  behavior already satisfies these contracts; the new tests detect their removal.
- The cancellation regression has an independent two-second assertion deadline
  and owns acknowledged process handles before cancellation. Its cleanup does not
  rely on the client path being mutated. An acknowledged descendant distinguishes
  `Kill(entireProcessTree: true)` from killing only the direct child.
- Direct-child exit is required when cancellation returns. Descendant exit gets a
  separate two-second settling interval. The first Linux immediate-descendant
  assertion failed (79 pass, one fail); the corrected bounded assertion passes.
  This follows the [documented asynchronous tree-termination contract](https://learn.microsoft.com/en-us/dotnet/api/system.diagnostics.process.kill?view=net-10.0).
- The nonnegative pending-count loop uses a property pattern. This preserves its
  behavior while avoiding Stryker 5.0.0's invalid `Count` property to `Sum` method
  group instrumentation and collateral rollback of a valid equality mutation.
  Current guard negation and `>= 0` variants are killed; generated `< 0` remains
  unviable. A plain C# build independently rejects it with CS8518.
- A Windows fixture observes the spawned process's console-window state. Both the
  normal and `CreateNoWindow=false` counterfactual have no console window on this
  headless host. This observation cannot qualify the flag for an interactive or
  console-attached parent; its survivor remains explicitly open.

## Executed native campaigns

| Windows full run | Test/source scope | Generated | Killed | Survived | Timeout | Compile error | Uncovered | Ignored |
|---|---|---:|---:|---:|---:|---:|---:|---:|
| 20261030T124250Z-c9f5945d | historical starting cohort | 201 | 169 | 10 | 6 | 2 | 1 | 13 |
| 20261030T130820Z-4be8c60f | six new field/diagnostic cases | 201 | 175 | 4 | 6 | 2 | 1 | 13 |
| 20261030T160505Z-83358ae8 | pattern guard, bounded direct-child test | 202 | 180 | 4 | 3 | 1 | 1 | 13 |
| 20261030T161459Z-cc10a65d | final acknowledged process-tree test | 202 | 181 | 3 | 3 | 1 | 1 | 13 |

The final full run completed in 503.44 seconds with native exit zero, no manual
intervention and zero workers left for the framework's reaper. Different 201/202
populations are not comparable mutation scores; the stable original-cohort map
records the specific catches. No score combines these runs.

A separate Linux Stryker 5.0.0 replay disabled coverage selection and selected the
two cancellation theory cases and cleanup source span. All four selected cleanup
mutations were killed: negated exit guard, deleted Kill, disabled tree Kill, and
deleted exit wait. The full generated report also retains one compile error and
197 ignored candidates; filtering does not avoid assembly-wide compilation.
This is focused evidence, not a full Linux managed mutation suite.

The first Linux mutation attempt failed its unmutated baseline with two broken
pipes. A direct fixture probe exposed missing apphost runtime discovery. Resolving
`DOTNET_ROOT` from the existing installed dotnet executable fixed the replay;
no new runtime was installed. Failed logs remain local alongside the successful
rooted run. Ordinary Linux `dotnet test` had already passed, demonstrating why the
standalone apphost environment needs its own check.

Native reports/log hashes, source hashes, frozen native inputs and exact candidate
columns are recorded in the JSON. The boolean cleanup survivor was the tree-Kill
argument, not the exit guard; descriptions without source columns are ambiguous.

## Remaining outcomes

These IDs refer to the final 202-candidate Windows report:

| ID | Actual outcome / disposition |
|---|---|
| 93 | Survived: headless console observation cannot distinguish CreateNoWindow; interactive Windows qualification pending. |
| 94 | NoCoverage: defensive null-return startup diagnostic unexecuted. Ordinary startup failures can throw instead; no kill inferred from source inspection. |
| 97 | Timeout: removing stdin Close leaves the fixture waiting for EOF. |
| 100 | CompileError: generated negative-count pattern fails plain compilation, CS8518; not a kill. |
| 104 | Timeout: removing pending-task removal produces an unbounded completion loop. |
| 182 | Survived on Windows, Killed in focused Linux replay: omitted process exit wait. Not equivalent. |
| 184 | Timeout: inverted EOF condition loops on zero-byte reads. |
| 196 | Survived: conditional decode-only BOM equivalence; reopen if preamble/output APIs are used. |

The three timeout cases are deliberate liveness faults. Native timeout detection
is retained separately from test-reported kills. Further independent outer limits
across transport/conformance tests remain useful work. The defensive null branch
and interactive Windows process behavior are not qualified by these finite runs.
No broad mutation suppression or score threshold change was added.

The BOM disposition depends on using this instance only for decoding. The
[constructor's preamble flag](https://learn.microsoft.com/en-us/dotnet/api/system.text.utf8encoding.-ctor?view=net-10.0)
controls preamble emission; it does not replace the separate strict-decoding flag.

## Verification and review

- Windows and Linux: 80 managed tests passed, zero failed/skipped, including the
  real-engine conformance test and existing FsCheck property (MaxTest=256,
  replay `362,20261027`). The Windows-only console fact returns before assertions
  on Linux; its nominal pass does not supply Linux console evidence.
- Windows [Coverlet](managed-20261030-coverlet.json) and Linux
  [Coverlet](managed-20261030-linux-coverage.json): owned client 171/179 lines
  (95.53%), 112/114 branches (98.25%). SDKs 10.0.403 and 10.0.402 respectively.
  The new property pattern changes branch mapping; historical 111/112 is not a
  directly comparable trend. Generated, fixture/test and vendor code are excluded.
- Separate Windows [fixture coverage](managed-20261030-fixture-coverage.json),
  dotnet-coverage 18.12: 54/56 lines and 48/50 branches. Duplicate async/class
  mappings use per-line maxima; this is not complete path coverage or native
  kernel-API instrumentation. Its collection reran all 80 tests successfully.
- Real StrictDoc parsing and Sphinx warnings-as-errors are recorded separately in
  [requirements evidence](managed-20261030-requirements.json) and
  [engineering evidence](engineering-docs.json). MAN-004 and MUT-CLEANUP-001
  remain Draft; bounded evidence does not close the wider M1 requirements.
- Internal AI-assisted read-only source review found no blocking issue. Accepted
  follow-up: fault injection before parent acknowledgement is still unqualified;
  ownership handles are acquired after acknowledgement. The fixture's general
  startup-failure containment is not established by the successful handshake tests.

Malformed cases are deterministic. No new coverage-guided fuzz campaign, native
Rust coverage measurement, full-duration soak, CI dispatch or push occurred.
Numerical Rust production source is unchanged. This qualifies a finite managed
transport/test slice, not full SPICE or production containment.

## Reproduction

Use the existing pinned host/mutation setup in
[mutation-frameworks.md](../docs/mutation-frameworks.md). From a checked-out root:

```powershell
if (-not (Test-Path -LiteralPath scripts -PathType Container)) { throw 'Wrong checkout' }
if (-not (Test-Path -LiteralPath dotnet -PathType Container)) { throw 'Managed sources missing' }
[Environment]::CurrentDirectory = (Get-Location).Path
# Local/portable Linux runtimes need explicit apphost discovery for Stryker children.
if (-not $IsWindows) {
    $hostFile = Get-Item -LiteralPath (Get-Command dotnet).Source
    $linked = $hostFile.ResolveLinkTarget($true)
    if ($null -ne $linked) { $hostFile = $linked }
    $env:DOTNET_ROOT = $hostFile.Directory.FullName
    if (-not (Test-Path -LiteralPath $env:DOTNET_ROOT -PathType Container)) { throw 'Runtime missing' }
}
dotnet test dotnet/Simulator.Client.Tests/Simulator.Client.Tests.csproj
dotnet run --file scripts/ManagedCoverage.cs
dotnet run --file scripts/FrameworkMutations.cs -- --tool dotnet --minutes 25 --workers 1
```

The Windows native dependency used the existing GNU toolchain and frozen engine
identified in the manifest. Reproduce those inputs/pins for identical provenance.
The focused Linux config, including exact character span and test filter, is in
the triage JSON; recalculate spans if source bytes change. Add `--log-to-file`
when invoking native Stryker directly for its framework trace diagnostics.

Raw local evidence includes the four `work/framework-mutations/<run>` directories,
`work/managed-tree-settling-mutant-red.log`, `work/managed-tree-green.log`,
`work/managed-cleanup-linux-native.log`, `work/managed-cleanup-linux-baseline-probe.log`,
`work/managed-cleanup-linux-native-rooted.log`, and the Windows/Linux coverage logs.
Compiler-characterization commands and errors are retained in `work/managed-*-compile.log`.
Large raw artifacts stay local under the existing evidence policy.
