# Whole-corpus comparisons, issues and burndown

This campaign accounts for every discovered upstream deck, including unsupported,
rejected, partial, failed and unfinished work. Five paired benchmark cases are
not a whole-corpus compatibility result.

From an existing Spice checkout and existing ngspice installation:

```powershell
$root=(Get-Location).Path
$corpus='C:/dev/ngspice-ngspice' # replace with the existing upstream checkout
$ngspice='C:/dev/ngspice-install/bin/ngspice.exe' # Linux: existing executable
foreach($directory in @($root,$corpus)) {
    if(-not(Test-Path -LiteralPath $directory -PathType Container)){throw "Missing: $directory"}
}
if(-not(Test-Path -LiteralPath $ngspice -PathType Leaf)){throw 'ngspice missing'}
& (Join-Path $root 'scripts/Run-CorpusComparison.ps1') -CorpusRoot $corpus -NgspicePath $ngspice -BudgetSeconds 1800
```

The wrapper incrementally builds the release engine and NativeAOT helper using
existing Windows/Linux prerequisites. `-Managed` explicitly chooses managed
execution. Default publishing creates **one GitHub results issue per run** using
authenticated `gh`, complete arguments and `--body-file`. Origin selects the
repository. `-Offline` suppresses publishing for tests. No source push or CI is
inferred. Missing authentication/publishing failure is recorded, never uploaded.

## Inventory and outcomes

Default `tracked` mode examines all Git-tracked `.cir/.sp/.ckt/.net/.deck/.spice/.inp`
files throughout the upstream repository, plus bounded extensionless text with
an `.end` record. It excludes untracked build artifacts/duplicate trees. Explicit
`-InventoryMode files` scans local regular files instead. Links are not
intentionally followed; missing/linked tracked decks remain visible. Unclassified
extensionless assets are listed separately. Other extensions (models, scripts,
documentation/data) are not silently counted as circuits.

Each deck goes through the real Rust library importer, exposed as
`--import-ngspice`. Import returns a typed request, without simulation or control
execution. Diagnostics retain actual error classes: an import rejection is not
proof that the upstream deck is malformed. Lexical feature hints describe
co-occurrence, not isolated root causes.

Accepted declarative decks execute in scratch space with user spiceinit disabled.
The observation copy removes print/plot/probe cards and appends named node-voltage
and supported V/L/E/H MNA branch current prints, adding `.op` only for inferred
default OP. Equations/analysis parameters
are otherwise retained. External control/resource directives are excluded.
Original/executed/request hashes and edits remain explicit.

LU and faer execute independently. Policy v4 compares all representable node
voltages and supported V/L/E/H MNA branch currents, at most 200 representative
coordinates each including endpoints. Bounds are 1e-5 V or 1e-9 A absolute plus
1e-4 relative complex norm. Current is p-to-n positive, and ngspice's exact
source#branch table alias is bound to the requested i(source). Both the manifest
quantity and unit must match. JSON schema 2 stores the unit and absolute bound
with each signal's discrepancy/witness. The current bound participates in the
history fingerprint; v1/v2/v3 reports remain historical evidence.

AC matches printed frequency within 1e-12 + 5e-6*f Hz without phasor interpolation.
Transient interiors use linear interpolation; at most four adjacent doubles may
align an out-of-table endpoint. Larger excursions fail. Other terminal currents and global waveform error remain unqualified. Both
providers must agree. Additional declared analyses not compared by the one-
analysis importer produce partial_comparison, not a pass.

The issue groups diagnostics, example paths and overlapping feature counts into
triage checklists. First minimize a reproducer, then implement a qualified
contract or document exclusion. The earlier `1ns` scalar gap is fixed; an OP
source with inactive AC metadata
supplied NGC-034, now implemented and checked as a bounded source contract.

## Reports, history and failure handling

Default output is `evidence/ngspice-corpus`: JSON/Markdown/SVG per run,
`history.json`, and updated `burndown.svg`. Use `-Output` for a separate experiment.
Raw requests/decks/stdout/stderr/oracle/publisher logs remain local under ignored
`build/corpus/<run-id>`. No upstream deck bodies or model assets are copied into Git.

Burndown is **total minus compared passes**. Unsupported, rejected, partial,
failed and unfinished all remain work. Series identities include original deck
hashes, inventory mode, comparison policy/tolerances and oracle executable.
Unknown hashes prevent claiming comparable history. The first point is a baseline;
the five-case benchmark is not fabricated as a whole-corpus history point.
Issues include a numeric history table and Mermaid chart; SVG is the standalone
local export. Numeric evidence does not depend on remote chart rendering.

All candidate rows exist before child execution. Complete JSON replacements are
checkpointed after each deck and code/input/executable hashes rechecked. This
preserves the prior complete checkpoint, not a guarantee against disk failures,
forced termination or permanent sharing denial. A killed process may leave its
last checkpoint marked running and cannot guarantee issue creation. Recoverable
harness failures are recorded and publication attempted. Inspect publisher logs
before retrying an ambiguous network failure to avoid duplicate issues.

Observation edits remove complete print/plot/probe logical records, including
continuations, while preserving device continuations. Child import timeouts are
execution failures even when they are the final deck.

Exit 0 means report completed, possibly with unsupported gaps; 1 execution/
comparison/provenance failure; 2 budget/cancellation unfinished; 3 publication
failure; 4 setup/harness failure. **Zero is not full-SPICE qualification.** Work
budget covers importer/reference/simulator execution; build/inventory hashing
and publishing are separate setup/finalization costs.

Verification is in `evidence/corpus-matrix-verification.json`. Coverage scopes,
seeds, survivors and unavailable platforms remain explicit. The runner reuses
maintained .NET facilities and existing table/process verification; no second
SPICE parser or runtime dependency was added. Primary references:
[GitHub CLI](https://cli.github.com/manual/gh_issue_create),
[.NET enumeration](https://learn.microsoft.com/dotnet/api/system.io.enumerationoptions),
[Stryker](https://stryker-mutator.io/docs/stryker-net/configuration/).

Common-cause follow-up and next priorities: [failure priorities](corpus-common-failure-priorities.md).

Comparison policy v2 aligns an out-of-table transient endpoint only within four
adjacent representable doubles (ULPs), with finite nonnegative coordinates.
Larger excursions fail; interior linear interpolation is unchanged. This handles
6.000000000000001e-9 versus ngspice's printed 6.000000e-9 without an absolute-time
epsilon. The policy change starts a separate burndown series.

Real oracle integration tests require explicit tool paths. After checking the
checkout, build directory and both executables exist, set SPICE_CORPUS_ENGINE,
SPICE_CORPUS_NGSPICE and SPICE_CORPUS_TEST_ROOT (an existing build directory),
then run dotnet test dotnet/Simulator.Benchmarks.Tests. Missing tool paths produce
explicit skipped oracle tests, rather than passing cases. Raw owned fixtures
stay under the configured build directory. See CorpusNumericalIntegration.

Bounded DC/AC metadata, title and atto numeric conventions are described in
[source metadata](ngspice-source-metadata.md).

The ordered rollback/current/analysis/model follow-up is documented in [the bounded contracts](corpus-followup-contracts.md). Named constant-R models do not imply semiconductor or geometry-model support.
