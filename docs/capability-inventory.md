# Corpus capability inventory

This is the next stage after structural syntax acceptance: measure which language and model contracts the samples require, without trying to simulate them. The generated ANTLR tree supplies observations; the existing isolated worker/acceptance workflow supplies input and source identity. No second lexer, parser dependency, numerical importer or model implementation was added.

[Results matrix](capability-inventory-results.md) · [Complete bounded JSON](../evidence/capability-inventory.json) · [Assessment baseline chart](../evidence/capability-inventory-burndown.svg)

The completed run covers **916/916 required inputs**, with eight explicit exclusions from 924 attempts. It observes 3,618 feature kinds. The final test suite passes **245 tests**, including 1,408 seeded FsCheck cases. Stryker follow-ups detect **80 of 84** original open identities; four conditional survivors remain documented. Selected aggregation coverage is 100% line / 98% branch; extraction is 98.57% line / 92.77% branch. These are separate class scopes, not whole-simulator coverage.

## Reproduce

From the checkout in PowerShell, after acquiring the pinned upstream samples described in [the corpus matrix](ngspice-corpus-matrix.md):

```powershell
$repo=(Get-Location).Path
if (!(Test-Path -LiteralPath (Join-Path $repo 'scripts') -PathType Container)) { throw 'Run from checkout root' }
$root=$env:CODEX_ARTIFACT_ROOT
if (!$root -or !(Test-Path -LiteralPath $root -PathType Container)) { throw 'Set CODEX_ARTIFACT_ROOT to an existing writable directory' }
$out=Join-Path $root ('Spice-capability-'+[guid]::NewGuid().ToString('N'))
# CorpusRoot is optional when the inventory still names the actual local checkout.
./scripts/Run-CapabilityInventory.ps1 -OutputDirectory $out -Workers 2 -WorkerSeconds 90 -Minutes 30
```

To use another machine, add `-CorpusRoot` with its existing ngspice source directory. The default inventory is the same 910 upstream rows used by syntax qualification; tracked repository decks are added automatically. The wrapper verifies the pinned ANTLR generator, builds to the selected output directory, runs strict UTF-8 recognition and the explicit adjudicated legacy replay, and requires the valid-input acceptance gate before generating `capabilities.json`, `.md` and `.svg`. It preserves existing output evidence instead of overwriting `primary.json`. It does not publish issues, run simulations, load includes or execute commands. This chat may publish the reviewed bounded result issue separately under the existing repository authorization.

Public API: `DeckParser.Parse(text, collectFeatures: true)` returns immutable `SyntaxFeature` occurrences and physical UTF-16 source ranges; ordinary calls leave `Features` empty. Rejected inputs never expose partial feature evidence. The worker groups occurrences into per-deck `FeatureTally` rows with a first location. `CapabilityInventory.FromReports` validates matching source/binary identities, explicit adjudications and legacy hashes before computing reach.

## Meaning of the output

- **Occurrences** count declarations/structures in recognized syntax. Nested subcircuit records are visited once; containers do not duplicate their descendants.
- **Decks** count distinct input identities containing a feature, regardless of occurrence count. Different filenames with identical bytes remain distinct inventory entries; no cross-file deduplication is implied.
- **Partial** means a documented restricted legacy-importer subset. It does not mean this ANTLR representation is already connected to the Rust core.
- **Unsupported** identifies a reviewed absent bounded-import execution contract. **Unassessed** means no reviewed mapping exists; it is not evidence of either support or an implementation defect.
- Bare unsigned numeric model LEVEL spellings are retained; signed, braced and other evaluated forms remain expression profiles. `unspecified`, `expression` and `multiple` do not infer ngspice defaults or evaluate parameters. Model parameter names are grouped by family; model binding, inheritance and effective parameter values remain future work.
- Call-shaped source forms are counted separately from nested function calls. Bare DC/AC qualifiers and source arguments are not a complete semantic source-profile inventory.
- Quoted text and control command bodies remain opaque. Only the first whitespace-delimited command heading is inventoried, not its arguments, aliases, nested language or executable meaning.

The assessment registry is [capability-support.json](../compat/ngspice/capability-support.json). Its evidence pins the committed `bc48c64` source baseline and explicitly excludes the unrelated local sheet-resistor draft. Missing registry keys remain unassessed. The bounded JSON retains all feature kinds, report/source/binary hashes and completion state; large per-deck worker results and raw logs stay local under the artifact policy.

The results table headed “planning gaps” is an assessment backlog, not proof that every listed construct is unimplemented. Directive and structure views can describe the same record. Already implemented bounded terminators and analysis profiles are explicitly marked partial so they do not lead that backlog. Missing assessments still require investigation before implementation.

The 3,618 observed keys are inventory buckets, not 3,618 separate implementation tasks. Many parameter and directive keys belong to one shared contract.

Counts overlap. A deck may require parameters, hierarchy, includes, behavioral expressions and several missing device families together. Therefore no row says how many circuits a single fix will unlock. Unused model definitions and subcircuits are counted; include contents are not loaded. These observations concern standalone titled inputs and do not establish a resolved circuit's active device mix.

## How to use the priorities

1. Build the typed, source-located semantic boundary: symbol/model tables, hierarchical parameter scope, subcircuit binding and explicit external-resource policy. Minimize representative cases before implementing each contract.
2. For large model families, use the family/LEVEL/parameter distribution to select an explicit bounded profile and revisit existing libraries/adapters before writing equations. Do not implement a broad MOS/BJT family from its name alone.
3. Connect already-supported components and analyses through the typed boundary, then qualify complete circuits against ngspice with voltages, currents and analysis-specific tolerances.

The assessment SVG establishes one comparable baseline of feature kinds. It deliberately does not reuse or reduce the separate numerical compatibility burndown. New comparable snapshots can record progress only when the inventory and assessment semantics remain comparable. Syntax acceptance alone closes no model or numerical requirements.

## Verification

[CAP-001/002](capability-inventory.sdoc) remain Draft. Tests cover extraction, source ranges, opaque languages, indentation/composite tokens, nested calls, prediction-strategy agreement, malformed inputs, identity corruption, overlap counts and unavailable replay evidence. See [bounded verification](../evidence/capability-inventory-verification.json) and [the latest inventory](../evidence/capability-inventory.json) for actual counts, coverage, mutation outcomes, seeds and limitations. Review found five issues; all were addressed with regression tests or pinned assessment provenance. No full SPICE, generated-grammar mutation, Linux, Native AOT, production memory-budget or numerical qualification is implied.

For local checks, use the existing test and pinned Stryker tooling in an isolated checkout (raw outputs belong under the artifact root):

```powershell
$repo=(Get-Location).Path
if (!(Test-Path -LiteralPath (Join-Path $repo 'dotnet/Simulator.Syntax.Tests') -PathType Container)) { throw 'Run from checkout root' }
$out=$env:CODEX_ARTIFACT_ROOT
if (!$out -or !(Test-Path -LiteralPath $out -PathType Container)) { throw 'Set an existing artifact root' }
$out=Join-Path $out ('Spice-capability-checks-'+[guid]::NewGuid().ToString('N'))
if (!(Test-Path -LiteralPath $out -PathType Container)) { New-Item -ItemType Directory -Path $out | Out-Null }
dotnet test ./dotnet/Simulator.Syntax.Tests --artifacts-path (Join-Path $out 'build') --collect:'XPlat Code Coverage' --results-directory (Join-Path $out 'coverage')
if ($LASTEXITCODE) { throw 'Syntax tests failed' }
dotnet tool run dotnet-stryker -- --config-file dotnet/Simulator.Syntax.Tests/inventory-stryker.json --test-project dotnet/Simulator.Syntax.Tests/Simulator.Syntax.Tests.csproj --output (Join-Path $out 'mutations') --skip-version-check
if ($LASTEXITCODE) { throw 'Inspect Stryker logs' }
```

The checked-in replay profile selects character spans of the recorded original open cohort, not a full new mutation scope. Regenerate it if the source changes. `scripts/SummarizeCapabilityMutations.cs` reconciles original and replay JSON by source, location, operator and replacement rather than merely trusting numeric mutant IDs. Timeouts, compile errors and excluded outcomes are never relabeled as kills. The study's mutation campaign used a minimal source snapshot on E:, preserving the active checkout.

### Remaining mutation evidence

The original 84 open identities reconcile to 80 detected by stronger tests and four surviving mutations. IDs 3 (nonempty-group First/FirstOrDefault), 123/126 (defensive checked overflow), and 183 (sorting both identity maps in the opposite direction) retain explicit conditional-equivalence arguments; none is relabeled a kill or a formal proof. The selected final replays contain no uncovered mutants or timeouts. Stryker also reports 90 unviable compile-error instrumentations across scopes; these are distinct from the successful product build and are not kills. Raw campaigns stay local; the bounded ledger preserves identities and separate scope counts.
