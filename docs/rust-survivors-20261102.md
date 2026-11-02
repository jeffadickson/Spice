# November 2 Rust survivor follow-up

Scope: the 17 missed mutants from overnight run `20261102T080055Z-25e64e76`,
Rust shard 10/16, at `f596636` plus the archived local changes. This follow-up
uses the clean source worktree, excluding unrelated main sheet-resistor work.
The cohort's affected production files must match the archived bytes before
replay. No broad mutation score or simulation-compatibility closure is inferred.

The approach is to isolate validation operands, challenge boundary equality,
and assert independent physical or mathematical expectations rather than only
checking that an operation completed. Existing cargo-mutants 27.1.0 and proptest
1.9.0 are reused; no new mutation engine or dependency is needed.

Oracles:

- AC componentwise backward error at exactly its inclusive threshold.
- Dimensionally correct error scaling for the first branch-current unknown, and minimum-step failure
  at a nanosecond scale.
- Legacy BE auditing over multiple actual intervals, and the VCVS control law.
- Available audit metrics replacing unavailable evidence in either merge order.
- Separately invalid measurement times, assertion references and lower bounds, and an integral
  whose triangular interior knot determines its area.
- A balanced expression exceeding the node budget while below the depth limit.
- Equal logarithmic endpoints, a later interpolation segment, PWL initial value,
  deserialized Newton defaults, and negative time without source overrides.
- Correction accounting retaining its wrapped provider identity.

Tests are in `tests/overnight_survivors_20261102.rs` and private unit modules
in `src/adaptive.rs` and `src/newton.rs`. Mutation replay must retain raw
outcomes, exact names, source hashes, tool versions and logs on E:. Survivors
with the same stable contract may be reviewed as conditionally equivalent,
without inventing a kill or asserting formal equivalence. Invalid or
unattempted mutants remain unavailable evidence.

Execution results are recorded in `evidence/rust-survivors-20261102.json`.

## Equivalence boundary and scope

The AC real-amplitude disjunction mutation changes the diagnostic path, but an
empty identifier still fails independent-source lookup and a nonfinite real
amplitude still produces a nonfinite assembled RHS. With compiler-produced
plans, both cases return `InvalidInput` before bias/provider work. The public
contract does not stabilize those message strings. This is a reviewed
conditional-equivalence disposition, not a kill or a formal proof. Reopen it
if plan construction, source assembly or the error contract changes.

The review initially mapped three mutants to the wrong nearby checks. Inspection
of every stored native diff corrected the tests to isolate the actual first
branch-current unit boundary, ValueAt time disjunction, and assertion
reference/lower-bound disjunction. The first test-only baseline fixtures also
misused the legacy importer's accepted PWL spelling and assumed a physical audit
could claim global accuracy. Those attempts are retained as failed harness/test
baselines; no product fix or mutant kill is attributed to them.

The inventory gate rejected an initial 19-name listing before executing mutants:
cargo-mutants included two field deletions outside the regular-expression
selection. The final campaign uses separate exact inventories: 16 original
non-importer mutants, then the original importer mutant plus two explicitly
identified supplemental time-grid field deletions. All actual outcomes must be
reported separately; the extra candidates do not enlarge the original cohort.

A read-only AI code review checked source/diff mappings, not external expert
review or independent execution. The review correction is recorded above.
`CONS-MERGE-001` remains Draft: the new ordering test alone does not establish
all differing-tolerance or nonfinite-severity combinations in that requirement.

No production algorithm or parser behavior changed. Sheet-resistor work on main
is excluded from this clean checkout. The original overnight shard's 71 invalid
and 51 unattempted candidates, .NET mutations, Linux/GPU execution, broader
parser/model support and full-SPICE qualification are outside this follow-up.

## Reproduction

Use the pinned tool setup and existing `scripts/FrameworkMutations.cs` rather
than a custom mutation engine. From a verified, isolated snapshot on E: that
contains Cargo/global SDK files, `src`, `tests`, `examples`, `compat`, the selection JSON files under `evidence`, and FrameworkMutations.cs/HostTools.ps1 under `scripts`:

```powershell
$checkout = (Get-Location).Path
if (-not (Test-Path -LiteralPath $checkout -PathType Container)) { throw 'Missing snapshot' }
$coreNames = Join-Path $checkout 'evidence/rust-survivors-20261102-core-names.json'
$importerNames = Join-Path $checkout 'evidence/rust-survivors-20261102-importer-names.json'
foreach ($path in @($coreNames, $importerNames)) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Missing selection: $path" }
}
$hostTools = Join-Path $checkout 'scripts/HostTools.ps1'
if (-not (Test-Path -LiteralPath $hostTools -PathType Leaf)) { throw 'Missing host tools' }
. $hostTools
Import-SpiceTools -Root $checkout
if ($IsWindows) { Import-SpiceMsvc }
# Set SPICE_CARGO_MUTANTS to the existing pinned executable when the snapshot
# has no .tools directory; do not copy an executable from an untrusted source.
$mutator = if ($env:SPICE_CARGO_MUTANTS) { $env:SPICE_CARGO_MUTANTS } else {
    Join-Path $checkout ('.tools/mutation/cargo-mutants' + $(if ($IsWindows) { '.exe' } else { '' }))
}
if (-not (Test-Path -LiteralPath $mutator -PathType Leaf)) { throw 'Missing pinned cargo-mutants' }
$env:SPICE_CARGO_MUTANTS = $mutator
$env:PROPTEST_RNG_SEED = '2026110201'
$env:PROPTEST_CASES = '128'
dotnet run --file scripts/FrameworkMutations.cs -- --tool rust --minutes 45 --rust-test lib,overnight_survivors_20261102 --rust-file 'src/{ac,adaptive,api,audit,configured,experiment,expression,grid,measurement,newton,sources}.rs' --rust-names-file $coreNames --workers 2
if ($LASTEXITCODE) { throw 'Core replay failed; retain logs' }
dotnet run --file scripts/FrameworkMutations.cs -- --tool rust --minutes 20 --rust-test lib,overnight_survivors_20261102 --rust-file src/netlist.rs --rust-names-file $importerNames --workers 2
if ($LASTEXITCODE) { throw 'Importer replay failed; retain logs' }
```

The helper takes a new immutable source copy and validates the complete exact
native name inventory before mutation. `partial_timeout` still requires reading
the summary even when the helper exits zero. Reproduction on another platform
is not evidence of Windows/Linux equivalence until actually executed. Delivery
full regression and focused coverage use seed `2026110201`, 256 requested
proptest cases per property. No new long soak or coverage-guided fuzz campaign
is claimed; malformed/fuzz-smoke/property targets are in the full Rust suite.

## Completed results

| Scope | Killed | Raw survivors | Invalid / timeout / unattempted |
|---|---:|---:|---:|
| Original 17 Rust survivors | 16 | 1 conditional equivalent | 0 / 0 / 0 |
| Two supplemental importer field deletions | 2 | 0 | 0 / 0 / 0 |

Core replay took 2157.9998756 seconds. Exact native names, source/test digests,
phase durations, retained diff/log paths and tool outcomes are in the bounded
JSON. All affected production prefixes match the current checkout exactly;
the core snapshot predates the supplemental importer test and formatting, while the importer snapshot predates only formatting. Production behavior is unchanged.
The source/test/requirements checkpoint is `79caf69`.

Final Windows MSVC regression: **475 passed, one explicitly ignored endurance
batch**. Clippy passed with `-D warnings`; changed Rust files pass rustfmt.
StrictDoc 0.30.4 parsed 21 documents, 329 unique IDs and 317 resolved parents.
Focused integration coverage: **1989/6142 owned Rust lines (32.38%)**. Branch
coverage is unavailable; private unit-test objects, vendor/test code and full
regression coverage are outside that collection. Do not compare this denominator
to differently scoped earlier reports. Five paired ngspice fixtures pass with
LU and Faer, one warmup and three measured iterations. Rust is debug-built and
machine load was concurrent; memory metrics are unavailable and no solver speed
ranking is claimed. This does not expand the earlier 17-file frontend census.

The interpolation mutant also fails the proptest oracle with the retained
minimized input `left=0, middle=0, right=-71.22768681948112, numerator=1` (seed
2026110201). Deterministic tests already catch the wrong bracketing pair. The
counterexample belongs to a deliberately mutated program, not an unmutated
simulator failure.

Common causes were weak isolation of validation operands, ordinary test values
that missed equality/unit boundaries, and insufficient assertions on audit,
interpolation or imported-analysis semantics. The new tests detect those changes
without altering numerical code to improve a mutation score. One defense-in-depth
AC guard remains conditionally equivalent under the stated public contract.

Raw artifacts remain at `E:/Artifacts/Spice-rust-survivors-20261102T142355Z`.
Thirty-six original native outcome/inventory/diff/log files were copied and
checksum-verified there before the reusable overnight workspace can be replaced.
No raw build outputs or large logs are committed.
