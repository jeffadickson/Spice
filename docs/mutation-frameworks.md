# Generated mutation campaigns

The repository uses cargo-mutants 27.1.0 for Rust and Stryker.NET 5.0.0 for C#.
Windows campaigns use [short physical artifact paths](framework-physical-paths.md)
and launch the real transport fixture before entering Stryker's baseline.
These are developer tools, not simulator runtime dependencies. Maintained frameworks
run first in overnight campaigns; custom numerical regressions are supplemental,
explicitly selected checks. Historical 22/22 evidence is not a generated mutation score.

Focused Rust selection accepts lib and up to eight comma-separated integration
targets, for example `-RustTest lib,continuation_hardening`. Use `-Workers 1`
for a bounded concurrent qualification run. `-RustFilter limited_candidate`
selects names using the common Rust/.NET nonbacktracking regex subset (maximum
512 characters); the wrapper checks the actual native inventory before mutation.
Observed cargo-mutants 27.1.0 field-deletion selection outside a name filter causes
an explicit preflight failure, rather than silently testing a wider scope.

The dedicated Rust cache retains vendor dependencies but cleans the owned
circuit-simulator package before each baseline. A real stale-mutant baseline
failure motivated this rule; Cargo timestamp freshness alone was insufficient.
Cleanup refuses linked cache paths. Rust and .NET snapshots use separate ordered
locks and independent outputs; all-tool runs acquire both locks.

From PowerShell 7 at repository root:

    make mutation-tools
    ./scripts/Run-FrameworkMutations.ps1 -Minutes 60
    ./scripts/Run-FrameworkMutations.ps1 -Minutes 20 -Shard 3/16
    ./scripts/Run-FrameworkMutations.ps1 -Minutes 10 -RustFile src/quantity.rs -RustTest analyses
    ./scripts/Run-FrameworkMutations.ps1 -Minutes 60 -Tool rust
    ./scripts/Run-FrameworkMutations.ps1 -Minutes 20 -Tool dotnet
    make mutation-rust MINUTES=60
    make mutation-dotnet MINUTES=20
    make endurance MINUTES=300

Setup-MutationTools.ps1 -Install downloads the pinned official cargo-mutants
release for Windows/Linux x64, verifies its recorded upstream SHA-256, and restores
Stryker from the committed root dotnet-tools.json manifest. Verification-only setup
checks tool pins without downloads. Setup-Host and Start-RemoteRun include this
step by default; their explicit mutation skip excludes these tools. Initial OS
approval/Git authentication prerequisites remain in [remote-runs.md](remote-runs.md).

Tool selects all (default), rust or dotnet; skipped tools are explicit. The standalone framework supervisor has a 60-minute default (make uses MINUTES,
whose default is 300). It builds the real native test dependency, gives Stryker
40% of the remaining budget, then gives cargo-mutants the rest. A single-tool run gives that tool the remaining budget. The outer endurance
supervisor runs the targeted mutants first, assigns half its remaining time to
that framework supervisor, then executes seeded numerical/parser batches. Setup
and tool verification are outside the campaign clock. Process startup/cleanup can
add bounded overhead; completed duration is reported, not inferred from a target.

Rust defaults to owned src/*.rs and all generated candidates (shard 0/1). The default test scope is the full suite. An explicit RustTest integration-test name selects a narrower baseline and mutant test target, recorded as rust_test in the summary. Focused evidence does not establish full-suite mutation coverage. The
endurance workflow uses deterministic round-robin shard (seed modulo 16)/16;
indices are ZERO-BASED: 0/16 through 15/16. Reuse the exact source revision, shard,
filter and tool version to replay the selection. Native cargo-mutants reports
retain individual mutant locations, diffs and outcomes. Order is deterministic,
not shuffled. A rotating shard improves breadth but does not guarantee all
candidates finish; unattempted counts remain explicit.

Managed scope is Client.cs: transport, input/output bounds and result validation.
The xUnit project shares the existing 28 real-engine conformance checks and adds
completion-field and UTF-8 size-boundary cases. DTO/schema-only files, generated
JSON metadata and standalone C# tooling are outside the Stryker mutation scope.
Stryker runs against copied managed sources/binaries and a fixed native executable;
it does not mutate Rust. cargo-mutants uses its own source copies. Standalone runs
have an exclusive lock within their checkout; avoid launching two mutation tools
against the same output tree manually.

work/framework-mutations/<run>/summary.json records pins, scope, shard, requested
and elapsed budget, per-tool status, separate categories and native report/log
hashes. Native JSON reports, diffs, minimized/replay fixtures where applicable and
logs remain local. Endurance archives its summary and exports only bounded count
fields plus its hash. The Git result publisher verifies summary hash/metadata/counts
before adding these fields to the existing allowlisted remote manifest. Per-tool status and nullable native-report hashes distinguish unavailable outcome counts from measured zero.

Interpret categories separately: killed, survived, timeout, compile-error/invalid,
no-coverage, ignored, pending, unknown and unattempted. A timeout does not establish
a test-detected kill. Stryker's headline score can include timeouts; do not present
that score as a kill-only percentage. Survivors require review for missing tests,
equivalent behavior or meaningful specification gaps. Baseline failures are tooling
failures; deadline interruption is partial work. If interruption prevents a native
report, zero recorded categories mean unavailable counts, not zero candidates.
No universal mutation score or production qualification is claimed.

Official references: [cargo-mutants](https://mutants.rs/) and
[Stryker.NET configuration](https://stryker-mutator.io/docs/stryker-net/configuration/).
Requirements MUT-001/MUT-002 and actual evidence are in
[the qualification report](../evidence/framework-mutation-qualification.md).

Native framework logs/reports and build caches remain local and may grow with the campaign; the supervisors do not enforce an OS-wide disk or memory quota. Stream-log retention and exported manifests have separate bounds.


The initial Rust smoke exposed generated constructors that cannot compile for the SimResult alias. These remain unviable outcomes, not kills or a test score. Framework tuning for that alias is still pending; the targeted numerical mutants remain complementary coverage.


Standalone Stryker runs now copy the native engine and three conformance JSON
fixtures into a private run snapshot and verify hashes before/after execution.
Managed sources/build outputs remain isolated. `--snapshot-self-test` checks
original-file independence and deliberate snapshot-tamper rejection. Native input
hashes are retained in the framework summary. This is input identity evidence,
not operating-system sandboxing or full helper mutation coverage.

For bounded survivor replay, `--rust-names-file <JSON-array>` selects 1–64 unique
exact native names and requires the native inventory to match before execution.
It cannot be combined with `--rust-filter` or a non-default shard. The selection
hash and names are retained in the summary. `Prepare-SurvivorReplay.cs` relocates
the original cohort only when its five-line source context has one unchanged
match; changed or ambiguous context requires human review. Stable identities live
in `evidence/overnight-survivor-triage.json`.

Pinned cargo-mutants 27.1.0 emits struct-field deletions despite regex selection.
The October 27 replay explicitly lists two supplemental field deletions so the
inventory can still be checked exactly. They are reported separately from the
35-survivor cohort. The first rejected preflight and upstream limitation are
recorded in `evidence/triage-native-selection-workaround.json`; no escaped
candidate is silently accepted and no tool upgrade or mutation suppression is
implied. Equivalent or masked survivors need documented reasoning and remain
separate from test-detected kills.

The relocation helper now accepts 1–64 retained entries instead of requiring the
first historical cohort's count of 35. It checks the count before source-file
walking; an empty, oversized or ambiguous cohort is rejected. Preparation does
not execute mutations or assign equivalence. `tests/SurvivorReplay.Tests.ps1`
checks a small synthetic cohort and rejection without inventory modification.

To repeat the full retained cohort on the current source from an existing checkout:

```powershell
$checkout=(& git rev-parse --show-toplevel).Trim()
if($LASTEXITCODE -ne 0 -or -not(Test-Path -LiteralPath $checkout -PathType Container)){throw 'Existing checkout unavailable'}
Set-Location -LiteralPath $checkout
dotnet run --file scripts/FrameworkMutations.cs -- --tool rust --minutes 45 --workers 1 --rust-test lib,overnight_survivors,ngspice_compat --rust-names-file evidence/overnight-survivor-current-names.json
if($LASTEXITCODE -ne 0){throw 'Inspect the emitted framework summary and native logs'}
```

Use the current-name file for this command. The initial and retry selection
files preserve historical subsets and source locations; they are not interchangeable
with the final source. Future source edits can invalidate exact locations and
require the checked relocation process again. A completed framework run can
contain survivors or unviable mutants; inspect its categories before drawing any
conclusion about detection. Run this serially with other compilation-heavy jobs
on memory-constrained hosts. Windows error 1455 and linker allocation failures
invalidate compile-based conclusions and require a healthy replay.
The `ngspice_compat` suite covers the two supplemental importer-field deletions;
omitting it from a focused survivor run can make those extras survive even though
the full regression suite already checks both imported time parameters.

## Resumable follow-up and test-linking profile

`Run-FrameworkMutations.ps1` accepts `-RustProfile mutants` and `-TestWorkers N` independently of compiler `-Workers N`. The default remains Cargo's `test` profile and inherited worker count. The opt-in profile uses unoptimized code and normal assertions, debug symbols off, incremental compilation off, and16codegen units. Disabling incremental compilation avoids exFAT's observed hard-link fallback; this is not a measured claim that every campaign is faster. Record the effective profile and test scope with timings.

`-RustTest lib,mutation_suite` explicitly enables the `mutation-suite` feature and links all standalone integration sources into one target. `MutationSuite.Tests.ps1` checks inventory completeness; the CSC self-spawned property test verifies its qualified selector and a256-case completion marker, including a negative missing-selector test. Normal product and test selection do not enable the aggregate. Platform-specific ignored/unavailable tests remain separate from passing tests.

The framework isolates mutation targets under its physical artifact root, serializes them with an exclusive cache lock, and cleans the owned Cargo package in the selected profile before the baseline. Normal qualification targets must never be passed as mutation caches. Cache cleanup has at most300seconds, bounded further by remaining campaign time; failure or expiration is setup failure and leaves candidates unattempted. A simultaneous invocation fails closed on its checkout/cache lock; callers must serialize work rather than assume it queues automatically. Compile an immutable helper once into a unique E: artifact directory when concurrent file-app build caches would otherwise conflict, then invoke that exact DLL and retain its source/build hashes.

A library-first replay can quickly detect candidates using the real cargo-mutants framework. A library-tier miss is only a scoped miss: replay it against `lib,mutation_suite` before classifying a full-suite survivor. Preserve original candidate identity and source, native reports and test scopes across retries; deduplicate completed original candidates and report explicit supplementary candidates separately. In particular, the documented cargo-mutants27.1 struct-field-deletion extras may survive a narrow test target and must not be assumed compile-invalid. Current-source regression kills do not retroactively change old-source raw outcomes.

`Observe-MutationRun.ps1` is a Windows mutation-only observer with a deadline and owned-child cleanup. It records private bytes without imposing memory caps or free-RAM gates. Windows manages paging. If mutation workers actually grow out of control, record the observed growth, PID/start identity, intervention and incomplete outcomes. Descendant discovery is sampled; a child hidden behind an unobserved short-lived parent is outside its retained-handle cleanup guarantee. Pester's parent-process coverage cannot measure independently launched PowerShell bodies; retain functional process tests and report that coverage limitation rather than treating unmeasured code as covered.