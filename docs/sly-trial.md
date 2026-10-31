# Bounded Sly 3.8.1 experiment

The user requested a bounded Sly comparison after questioning the complexity
of ANTLR. This is an executable reuse assessment, not approval to replace the
current frontend. The user subsequently confirmed retaining ANTLR. Production syntax acceptance remains the independently
qualified ANTLR implementation and its 916-valid-input gate.

## Scope and approach

`experiments/SlyTrial` defines two independent attribute-based fragment grammars
(XSPICE-shaped ports and arithmetic expressions) and a generic-lexer mode probe
for node/expression contexts and inert control records. It uses the actual
**sly 3.8.1** NuGet package, with package hashes in lock files. Production
`Simulator.Syntax.dll` is referenced only by the comparison executable; candidate
recognition methods use Sly. No product project takes a Sly dependency.

The experiment was bounded to these fragments. It adds no whole-deck parser,
include loader, simulator, handwritten recognizer, custom lexer extension,
Java generation step for the candidate, or migration of existing APIs. The
regex fragment grammars and generic-mode probe are deliberately separate;
their integration into one deck lexer has not been attempted or qualified.

The typed production actions construct syntax nodes and ranges, never physical
values. Parsing is followed by validation and application operations in the
larger architecture; recognizing a word does not make it a supported device.

## Findings

- All 23 shared fixed expectations matched for both the candidate and ANTLR.
  These include accepted and malformed fragments, not 23 complete circuits.
- A direct `Name port+ Name` production consumed the model as a port in the
  initial candidate. The first implementation run had 14 passing and six
  failing tests. Factoring the trailing named-node/model sequence with Sly
  productions fixed the shared cases; no handwritten parsing machine was added.
- Expression precedence, nested calls, malformed-input rejection, physical
  token offsets, built-in mode switching and inert control text are executable.
  Thus lexer modes are not an ANTLR-exclusive reason to choose it.
- A skipped continuation containing a comment preserves the absolute UTF-16
  token offset, but reports line 1 where the original fragment is on zero-based
  line 2. A physical-position adapter would be needed. Our ANTLR wrapper already
  uses a separate physical-position map for its own newline conventions.
- The generic control probe rejects `.endc_extra` as a command followed by a
  real `.endc`; ANTLR preserves it successfully. The simple sugar-token/end-marker
  definition needs a better boundary rule. This is an observed candidate grammar
  integration gap, not proof that Sly cannot express the language.
- Five additional challenge cases exercise omitted productions: typed/wrapped
  vectors, numeric model references, ternary and Boolean expressions. Their
  rejection describes this small grammar's scope, not a parser-library ranking.

The initial and final outcomes, diagnostics, source-location observations and
verification metrics are retained in `evidence/sly-trial.json`. Raw build,
coverage and mutation artifacts remain on E:. No Linux, NativeAOT, adversarial
depth or performance qualification is claimed. Randomized text is bounded to
128 UTF-16 characters; no coverage-guided fuzz campaign ran.

Final execution: **30 unit tests passed**, including 128 generated differential
pairs (FsCheck replay `551,20261031`) and 128 bounded malformed strings (replay
`552,20261031`). The comparison report separately contains 23 shared expectations
and six challenge cases; these overlap the unit tests and are not additive.

Coverage scopes are separate: the full experiment assembly under unit tests
measured 36.22% line / 28.26% branch because its CLI and report fixture list are
not exercised by that collector. The candidate API class itself measured 100%
line / 90% branch. The independently collected CLI run measured 91.04% line /
74.29% branch for the experiment assembly. Neither collector measures candidate
grammar conformance or numerical-core correctness.

Focused Stryker.NET mutation testing of `Trial.cs` initially killed 12 mutants,
left three survivors and four uncovered. Stronger lexical-diagnostic and failed
result tests increased the final result to **15 killed, two survivors, two
uncovered and three ignored**. The surviving mutations change an error-list
separator and a defensive failure-result null guard. Their effect was not
distinguished by the observed inputs; no general equivalence proof is claimed.
The untested paths concern construction failure of the fixed valid grammars.
The tool also reported 79 compile-error mutants in the excluded CLI source and
148 ignored mutants in the excluded fixture/grammar files. These are not kills
or tested candidate grammar mutations. No third-party parser-machine or grammar
production mutation campaign ran.

## Recommendation

Sly is a credible candidate for compact .NET DSLs, and the annotation model fits
the user's preference. ANTLR's superiority was not established by our original
design assessment. This experiment supplies actual evidence that Sly handles a
useful fragment subset with less generator setup, while showing integration
work that still needs to be measured.

Keep ANTLR selected for now. A replacement decision requires a single integrated
Sly frontend, resolution of the two observed integration gaps, equivalent
diagnostics/resource bounds and the unchanged complete inventory gate. Passing
this small experiment is not permission to begin that larger migration.

## Reproduction

Run in an existing checkout with the pinned .NET SDK and the existing ANTLR
baseline already built (`scripts/Initialize-SyntaxParser.ps1`, then
`dotnet build dotnet/Simulator.Syntax -p:RestoreLockedMode=true`). In PowerShell:

```powershell
$repository = (Get-Location).Path
if (!(Test-Path -LiteralPath (Join-Path $repository 'experiments/SlyTrial') -PathType Container)) { throw 'Run from repository root' }
$artifacts = $env:CODEX_ARTIFACT_ROOT
if (!$artifacts) { $artifacts = [Environment]::GetEnvironmentVariable('CODEX_ARTIFACT_ROOT','User') }
if (!$artifacts -or !(Test-Path -LiteralPath $artifacts -PathType Container)) { throw 'Configure an existing artifact root first' }
foreach ($part in @('Spice','sly-trial',('manual-'+[guid]::NewGuid().ToString('N')))) {
    if (!(Test-Path -LiteralPath $artifacts -PathType Container)) { throw 'Missing artifact parent' }
    $artifacts = Join-Path $artifacts $part
    if (!(Test-Path -LiteralPath $artifacts -PathType Container)) { New-Item -ItemType Directory -Path $artifacts | Out-Null }
}
$env:TEMP = $artifacts
$env:TMP = $artifacts
dotnet test ./experiments/SlyTrial.Tests --artifacts-path (Join-Path $artifacts 'build') -p:RestoreLockedMode=true --collect 'XPlat Code Coverage' --results-directory (Join-Path $artifacts 'coverage')
dotnet (Join-Path $artifacts 'build/bin/SlyTrial/debug/SlyTrial.dll') $repository (Join-Path $artifacts 'comparison.json')
```

The binary reference preserves the baseline during this experiment. It is not
a portable package/API design for a future product frontend. Candidate tests
and comparison reports identify different verification scopes; do not combine
their case counts or compare their coverage with numerical-core coverage.

## Primary references

- [Sly repository and documentation](https://github.com/b3b00/csly), inspected at
  `1bac89b338b2ed059b6f02eb39835bba32847120`.
- [NuGet sly 3.8.1](https://www.nuget.org/packages/sly/3.8.1), MIT; the resolved
  dependency graph and package content hashes are in the experiment lock files.
- [Lexer mode attributes](https://github.com/b3b00/csly/blob/1bac89b338b2ed059b6f02eb39835bba32847120/src/sly/lexer/attributes/ModeAttribute.cs)
  and [mode tests](https://github.com/b3b00/csly/blob/1bac89b338b2ed059b6f02eb39835bba32847120/tests/ParserTests/lexer/LexerModesTest.cs).
- [Annotated expression example](https://github.com/b3b00/csly/blob/1bac89b338b2ed059b6f02eb39835bba32847120/src/samples/SimpleExpressionParser/SimpleExpressionParser.cs).

Third-party source was inspected as a reference and is not vendored into this
experiment. Fixture forms come from the existing repository parser-family
tests and the previously pinned ngspice examples; they are not copied full decks.
