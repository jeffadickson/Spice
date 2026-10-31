# Issue #11: sample grammar qualification

The parser remains a separate generated ANTLR 4.13.1 frontend. No simulation
support, include loading, model resolution or control execution was added.
The original three-reference framework decision remains in `parser-only-plan.md`.

## Completed acceptance

All **916 declared valid inputs are structurally accepted**, with zero
outstanding inputs in `evidence/parser-gap-acceptance.json`. The primary run
attempted all 924 inputs (910 upstream inventory rows plus 14 repository decks)
in 210.76 seconds, using two workers, a 90-second per-input budget, 4 MiB input
and 2,000,000-token limits. Its actual outcomes were:

| Strict UTF-8 primary outcome | Upstream | Repository | Total |
|---|---:|---:|---:|
| Structurally accepted | 898 | 14 | 912 |
| Intentional syntax-error fixtures or prose documents | 8 | 0 | 8 |
| Encoding refused | 4 | 0 | 4 |
| Grammar gaps, timeout, resource refusal, worker failure, unattempted | 0 | 0 | 0 |

The eight exclusions are six explicitly evidenced malformed fixtures and two
README documents. They are pinned to input hashes and expected outcomes in
`compat/ngspice/parser-input-adjudications.json`. None is counted as a valid
sample pass. All four legacy-byte decks then passed an explicit byte-preserving
replay using identical source and DLL hashes; the replay's 14 repository decks
also passed (18/18). Their strict UTF-8 refusals remain in the primary report.

The tested working tree was captured at base `6ac4de8`, with scoped source
hashes matching implementation commit `689e061` at publication. Corpus HEAD
was `e8285746c73a8229cf922487c8ccfcf6bea61bce`, with a dirty local corpus;
per-input hashes, rather than the revision alone, identify the tested bytes.
`parser-gap-corpus.json` and `parser-gap-legacy-replay.json` retain bounded rows,
source/DLL identity, profiles and actual outcomes. Local absolute roots are
redacted; the gate was rerun on those published reports successfully.

The updated `evidence/parser-only-burndown.svg` shows the same declared valid
inventory's acceptance backlog falling from **423 to zero**. This is a syntax
acceptance chart, not a simulation or complete grammar-conformance chart.

## Implemented families

XSPICE ports support typed scalar/differential/vector/inverted/null connections
and custom connection types. Common blockers addressed include numeric model
and subcircuit references, signed/Unicode/bus nodes, filesystem paths, dollar
comments with reserved PSpice namespaces, continuation comments, complex pairs,
CIDER flags, OSDI/CPL/TXL/URC cards, PSpice logic/delay U cards, three-terminal
VDMOS, bare B/.func expressions, optional function equals delimiters, behavioral parameter tails and concatenated parameter expressions.
Legacy outer model parentheses are separators; nested expression, call and
connection delimiters remain checked. Explicit `*ng_script` files retain inert
command records. `.sndparam`, `.sndprint`, `.opt` and legacy `.endc` records
have named structural contracts rather than unknown-extension acceptance.

SLL recognition bails on errors and retries with fresh full LL recognition.
The two strategies have differential tests. Input, token, recursive-rule and
worker bounds remain explicit. Campaign limits are configurable independently
of the smaller public-library defaults. Long separator runs are coalesced by
the generated lexer to avoid quadratic failed-continuation lookahead.

## Verification and review

`evidence/parser-gap-verification.json` records 162 passing tests, source hashes,
Coverlet line/branch metrics and 1,024 bounded FsCheck cases with replay seeds.
Stryker.NET killed 12 eligible `ParserLimits.cs` mutants; two compile errors and
one ignored mutant are not kills. The earlier broad mutation survivor ledger remains historical and was not closed by this focused run. Generated grammar productions were not
mutation-tested. Coverage of generated recognition code is reported separately
from handwritten class rates; unvisited generated alternatives are not a
conformance result.

Handwritten syntax coverage is **141/142 lines and 113/120 branches**;
generated recognition coverage is **3829/5558 lines and 830/1160 branches**.
The independent CLI fixture campaign measured 86.39% line / 76.25% branch
coverage for the corpus executable. Twelve synthetic gate scenarios measured
98.92% line / 87.78% branch coverage for the acceptance helper. These scopes
are not interchangeable. PowerShell and chart-renderer coverage is unmeasured.
Stryker additionally recorded 41 compile errors and 185 ignored mutations
outside the selected profile file; these are not eligible tested mutants.

Real StrictDoc parsed the intended 16-document snapshot: 307 unique IDs and
295 resolved parent links. Sphinx 9.1.0 built with warnings as errors. The
snapshot excludes unrelated uncommitted sheet-resistor work, which was
preserved. Requirements remain Draft; this finite acceptance result does not
close broader ngspice language or numerical contracts.

An AI code review identified and prompted fixes for continuation loss at `$G_`
or `$D_` terminals, replay binary identity, authoritative inventory membership
and closed quoted expressions conflicting with PSpice logic constants.
Regression tests reproduce those cases. A complete suite also exposed slow
blank-line recognition, now covered by a 100,000-blank-line regression. Earlier
aborted test runs and cancelled/disk-full corpus attempts remain local raw
artifacts; they are not successful qualification runs.

The acceptance gate requires the authoritative inventory hash and exact
upstream/tracked-repository membership, complete reports, identical source and
binary maps for replays, and matching input hashes. Its twelve synthetic positive
and corruption scenarios test the gate, not real sample acceptance.

## Reproduction

From an existing checkout in PowerShell:

```powershell
$repository = (Get-Location).Path
$artifacts = '<existing output directory>'
if (!(Test-Path -LiteralPath $artifacts -PathType Container)) { throw 'Missing output directory' }
if (!(Test-Path -LiteralPath (Join-Path $repository 'scripts') -PathType Container)) { throw 'Run from checkout root' }
./scripts/Initialize-SyntaxParser.ps1
dotnet test ./dotnet/Simulator.Syntax.Tests --collect 'XPlat Code Coverage'
./scripts/Run-SyntaxCorpus.ps1 -CorpusRoot '<existing ngspice checkout>' -WorkerSeconds 90 -RequireValidSamples -Output (Join-Path $artifacts 'primary.json')
```

The input inventory is
`evidence/ngspice-corpus-tracked/20261031T055611Z-019f8631.json`.
The acceptance switch automatically creates the four-input legacy subset,
replays it with the same binaries and writes an acceptance report. It returns
failure for any outstanding valid input. Omit it for a report-only campaign.
For manual replay of legacy-byte inputs, make a subset inventory
retaining its corpus metadata, then run the **same parser binaries** with
`SPICE_PARSE_LEGACY_BYTES=1`; the CLI also adds tracked repository samples.
`Run-SyntaxCorpus.ps1 -LegacyBytes` provides the equivalent profile, but rebuilding
at a different Git revision can change assembly metadata and invalidate replay
binary identity. Preserve the built binary set between primary and replay.

```powershell
dotnet run --file ./scripts/ValidateSyntaxAcceptance.cs -- '<primary.json>' ./compat/ngspice/parser-input-adjudications.json ./evidence/ngspice-corpus-tracked/20261031T055611Z-019f8631.json $repository (Join-Path $artifacts 'acceptance.json') '<legacy-replay.json>'
dotnet run --file ./scripts/Test-SyntaxAcceptance.cs -- $repository $artifacts
dotnet run --file ./scripts/Test-SyntaxCorpus.cs -- $repository
```

Structural acceptance is intentionally weaker than complete ngspice grammar or
behavioral conformance. Quoted expressions and command bodies remain opaque;
parameter meanings, model availability, include resolution and numerical
correctness require later contracts. All execution evidence here is Windows
managed .NET; Linux, NativeAOT and the numerical core were not qualified.

