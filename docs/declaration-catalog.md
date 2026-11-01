# Declaration catalog: the first semantic frontend slice

The [delivery plan](semantic-frontend-plan.md) now follows observed corpus dependencies: declaration scopes, instance binding, parameter elaboration, external resources, the typed Rust bridge, then selected model profiles. Each stage has an independent exit gate and denominator. This slice implements declaration cataloguing and scoped lookup; it does not bind instances, choose model-aware terminal counts, flatten circuits, evaluate parameters or simulate anything.

## Implemented API

```csharp
var parsed = DeckParser.Parse(text, collectDeclarations: true);
var catalog = parsed.Declarations; // null when not requested or input rejected
var model = catalog!.Lookup(DeclarationKind.Model, "my_model", scopeId: 0);
var parameters = catalog.LookupParameter("gain", scopeId: 0);
```

Scopes, declarations, ordered parameter syntax, resources and diagnostics are immutable. Names use ordinal case-insensitive comparison. Model/subcircuit namespaces remain distinct. Forward definitions are visible; the nearest observed scope shadows ancestors. Same-scope named duplicates are ambiguous and retain both physical source locations under this explicit conservative policy. That is not an assertion that ngspice rejects all such inputs.

Parameter candidates retain header-default and .param assignment order and original UTF-16 source ranges. A found parameter means observed assignments, not an effective value. Repeated assignments are preserved; model parameters stay attached to their models. Subcircuit header arguments remain syntax, including potential markers; this slice does not infer a port list or instance arity from generic tails. .ends arguments are not header defaults.

Unresolved includes/library cards and conditional contexts make lookup provisional. Missing names become deferred where those inputs may affect availability. The current conservative flag applies globally, including unrelated scopes; opaque assignment targets similarly defer parameter lookup. This is not qualified ngspice visibility semantics. No files or commands are loaded/executed. Control/quoted languages remain opaque. Functions, model-dependent device terminals and instance references require later contracts.

`DeclarationSummary.From` checks the catalog's own indexes against all observed declarations/parameter assignments using grouped hash sets. This is an internal consistency check, not an independent semantic oracle. A public source example is [DeclarationCatalog.cs](../examples/DeclarationCatalog.cs).

## Reproduce

From the checkout in PowerShell, with the existing pinned tools and acquired upstream source:

```powershell
$repo=(Get-Location).Path
if (!(Test-Path -LiteralPath (Join-Path $repo 'scripts') -PathType Container)) { throw 'Run from checkout root' }
$root=$env:CODEX_ARTIFACT_ROOT
if (!$root -or !(Test-Path -LiteralPath $root -PathType Container)) { throw 'Set an existing writable CODEX_ARTIFACT_ROOT' }
$out=Join-Path $root ('Spice-declarations-'+[guid]::NewGuid().ToString('N'))
./scripts/Run-DeclarationCatalog.ps1 -OutputDirectory $out -Workers 2 -WorkerSeconds 90 -Minutes 30
# Add -CorpusRoot with an existing upstream checkout on another machine.
```

The wrapper runs strict UTF-8 syntax recognition, the explicit hash-matching legacy replay and the syntax acceptance gate, then separately requires catalog payloads and zero self-lookup failures. It checks impossible counter combinations. Missing metadata is unavailable evidence, not a catalog pass. Raw logs, per-input reports and builds stay under the chosen artifact directory. Existing primary evidence is not overwritten.

To run the example:

```powershell
$repo=(Get-Location).Path
if (!(Test-Path -LiteralPath (Join-Path $repo 'examples') -PathType Container)) { throw 'Run from checkout root' }
dotnet run --file ./examples/DeclarationCatalog.cs
if ($LASTEXITCODE) { throw 'Example failed' }
```

The example prints an ancestor model lookup, both unevaluated gain assignments and self-lookup counts. It executes .NET code with the pinned generated parser; it performs no simulation.

## Recorded corpus result

[Bounded evidence](../evidence/declaration-catalog.json) records **916/916 required inputs catalogued**, with eight adjudicated exclusions in the 924-attempt scope. The primary accepted 912 structures; four explicit legacy byte replays complete the cohort. Replays and source/binary identity remain independently checked.

- 30,070 scopes (916 roots plus 29,154 subcircuit bodies).
- 1,591 models and 578 scope-parameter assignments.
- 17,817 assigned model parameters, 293 unresolved resource cards and 189 deferred contexts.
- 31,323 self-lookups; zero consistency failures.
- 903 inputs with unambiguous observed definitions and 13 with duplicate-definition ambiguities. All 13 ambiguities remain visible; these are not invalid-circuit or numerical-failure verdicts.

These are declarations, including unused/conditional records, not an instantiated circuit's active device mix. The earlier capability/numerical burndowns remain separate. Requirements [SEM-CAT-001/002](declaration-catalog.sdoc) remain Draft. Verification scopes and limitations are in [the verification record](../evidence/declaration-catalog-verification.json).

## Next implementation

Specify typed X-call headers and model-aware device-reference contracts, then bind names and ports against this catalog. Use named ngspice fixtures for arity, numeric names, overrides, recursion and missing/ambiguous references. Parameter expression evaluation and include policy follow as separately qualified work. Reuse assessments must precede new numerical/model code; no broad semiconductor implementation begins from a family name alone.

## Verification and remaining mutation gaps

The suite passes 268 tests, including 384 new seeded FsCheck cases (701,20261101 and 702,20261101), for 1,792 configured successful property cases across the suite. Nine synthetic catalog-gate cases pass. The source example runs successfully. Real StrictDoc parses the intended 19-document snapshot (312 IDs / 300 parent links), and Sphinx builds with warnings as errors. Unrelated requirement/source edits were excluded from that publication snapshot and preserved.

Selected class coverage is 100% line / 100% branch for DeclarationCatalog and DeclarationExtraction. The separate SymbolComparer class is 100% line / 50% branch; DeclarationSummary is 100% line / 87.5% branch. The gate's independent process-tree collector measures 86.76% line / 76.09% branch. These are distinct scopes, not whole-project coverage or proof of correctness. Wrapper coverage is unmeasured.

Stryker follow-ups detect 11 of the original 18 open identities. Three survive: a comparer hash-collision oracle gap (not equivalence), a declaration-filter removal that currently preserves output but changes traversal overhead, and a header-NL boundary mutation with a conditional generated-tree invariant. Four defensive self-check failure mutations remain uncovered under valid immutable catalogs. The separate corruption tests challenge the reported-failure gate, not those private failure branches. The 107 compile-error instrumentations and all ignored outcomes remain separate from kills; no combined mutation score is asserted. See the bounded mutation ledger.
