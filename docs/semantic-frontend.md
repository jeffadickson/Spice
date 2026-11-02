# Instance binding, parameters and external resources

The selected ANTLR 4.13.1 frontend now has three explicit semantic operations.
Rust, existing solver adapters and the production numerical importer are retained.
Syntax acceptance, observed binding, effective parameters, supported circuit,
completed solve and oracle agreement remain separate outcomes.

```csharp
var resolved = ResourceResolver.Resolve(path, new ResourcePolicy([allowedRoot]));
// On failure, Text is null and Sources/Segments are empty; inspect Diagnostics.
var syntax = DeckParser.Parse(resolved.Text!, collectDeclarations: true);
var references = InstanceBinder.Bind(syntax.Declarations!);
var globals = ParameterEvaluator.Evaluate(syntax.Declarations!);
// A bound root X call uses globals. For a nested call, pass its evaluated
// enclosing-instance result, preserving lexical scope snapshots.
var values = ParameterEvaluator.ForInstance(syntax.Declarations!, rootCall, globals);
```

Check every preceding outcome before calling the next operation. These APIs do
not execute controls, load arbitrary plugins, invoke ngspice or create a Rust
circuit. `DeckParser.Parse` remains an I/O-free syntax operation.

## Binding contract

Generated contexts supply immutable X/D/Q/M/J/S/W headers, ordered positional
syntax, named assignments, source ranges and subcircuit formal ports. X selects
the final positional reference before assignments/`params:`; complex headers
are explicitly unsupported. Zero-port X calls are accepted. Lexical lookup is
ordinal case-insensitive, supports forward definitions and retains ambiguity.
Duplicate ports, arity mismatches, duplicate instance identifiers and potential
recursive expansion graphs remain visible. The graph includes unused definitions;
its diagnostics are not claims that an instantiated circuit was simulated.

Model profiles are D/2, Q/3-or-4 for NPN/PNP, M/4 for NMOS/PMOS, J/3 for NJF/PJF,
S/4 for SW and W/2 plus controlling-source syntax for CSW. Terminal ordinals are
zero based; a model terminal has no invented named formal source location.
Other families, variable terminal contracts, binning and positional flags require
further profiles. Family binding never implies support for its LEVEL or equations.

Unresolved resources/conditions defer binding conservatively. `BoundInstance`
results have an internal catalog identity and get-only fields; callers cannot
construct or alter a validated header/target through the public API. A binding
from a different catalog cannot be evaluated against local parameters.

This is symbolic ordered binding, not node renaming, hierarchy flattening,
global/ground alias translation or a compiled executable topology.

## Parameter contract

The evaluator reuses generated expression trees. There is no new lexer or parser.
It decodes finite SPICE numeric multipliers (M=milli, MEG=mega, MIL=25.4 micrometre
scale, T/G/K/U/N/P/F; trailing unit text is not dimensional analysis). Arithmetic,
comparisons, boolean AND before OR, right-associative conditional expressions and
abs/sqrt/exp/ln/sin/cos/min/max/pow use explicitly bounded finite numeric semantics.
Power precedes unary minus and chains associate left, matching the named binary
reference: `-2^2=-4`, `2^3^2=64`.

Single-quoted numeric expressions and braces are supported. Other string/quoted
languages, user-defined functions, vector indexing, predefined dynamic symbols
and general dialect functions are not qualified; unsupported/missing expressions
fail explicitly. Model-attached assignments and device values are not lowered
into a numerical model by this scope evaluator.

Within each lexical scope, the last assignment to a name wins. Dependencies are
simultaneous, forward references are supported and a self-reference is a cycle,
not an implicit prior-value update. Parent values retain the scope in which they
were evaluated. X overrides are evaluated in the caller; dependent defaults are
then recomputed in the callee using evaluated lexical ancestor snapshots. Separate
instances cannot overwrite one another's environments. Caller results belong to
the same catalog and lexical call scope; serialized/modified results are not
reusable execution contexts.

Overrides must name header defaults. Override/local `.param` collisions are
explicitly refused pending a broader ngspice precedence profile. Unknown data is
never replaced with zero. Failure publishes no partial effective-value dictionary.
Diagnostic ranges identify the failing dependency assignment. Limits are 4096
scope assignments per evaluation, 4096 override entries, dependency/expression
depth 64, 4096 expression characters and 1024 expression nodes, with inherited
parser bounds. These are bounded profiles, not full ngspice elaboration closure.

## Resource contract

Resolution requires explicit existing allowed roots. Paths resolve relative to
the referring file, are confined after normalization, and links/reparse traversal
are refused. UNC roots/targets are refused. UTF-8 is strict, with an optional UTF-8
BOM; source SHA256 covers original bytes and source text omits that BOM. Every
open, including repeated includes and the root, counts against file/byte budgets.
Defaults are 32 include levels, 128 opens and 8 MiB total bytes, plus parser limits.

Library selection supports uniquely named flat top-level sections, optional
matching `.endl` names and recursive external section references with cycle
checks. Nested section boundaries, malformed/unclosed/ambiguous sections,
conditional resource availability and one-argument library loading without a
selected section are refused under this profile. Sections inside subcircuits
are rejected rather than losing their enclosing scope.

The root `.end` clips inactive tail resources. `.end` in an included fragment or
inside a subcircuit is explicitly unsupported. Library sections are parsed for
structure, but includes in unselected sections are not opened. The observed
ngspice-47+ preprocessor does open such includes; this is a deliberate policy
difference, not a compatibility claim. The failed missing-unused-include oracle
trial is retained locally; the matched numerical fixture supplies valid files
in both sections.

`Sources` retains each decoded physical file/selected-section identity. Contiguous
`Segments` map expanded UTF-16 offsets to source index and original source offset.
Semantic ranges refer to the expanded input; map them through these segments to
recover physical file origins. Failed resolution returns no partial graph/text.
Filesystem races against a hostile concurrent writer are not sandboxed or formally
proved absent. E: is exFAT, so link-creation verification is explicitly skipped on
this host; replay that test on a supporting filesystem. No Linux result is claimed.

## Reuse decision

The existing generated parser and BCL immutable collections, Math, filesystem,
strict UTF-8 and SHA256 facilities satisfy these integration needs. New code is
source-located domain binding and policy, not a replacement recognition engine.
The prior pinned SpiceSharp 3.2.3 / SpiceSharpParser 3.4.1 executable assessment
remains the alternative baseline; integrating that runtime does not supply an
adapter to the retained immutable tree/Rust core. No new package was added.

Primary references: [ANTLR C# target at 4.13.1](https://github.com/antlr/antlr4/blob/4.13.1/doc/csharp-target.md),
[ngspice parameter scope](https://nmg.gitlab.io/ngspice-manual/circuitdescription/paramparametricnetlists/symbolscope.html),
[ngspice subcircuit parameters](https://nmg.gitlab.io/ngspice-manual/circuitdescription/paramparametricnetlists/subcircuitparameters.html)
and [SpiceSharpParser](https://github.com/SpiceSharp/SpiceSharpParser).
The manual contains historical default-parameter wording; actual named binary
trials, exact hashes and finite tested outcomes are authoritative for this evidence.

## Reproduction and storage

All new artifacts belong under E: on this Windows machine. This example validates
roots before creating its unique task directory; reuse the installed pinned jar.

```powershell
$repo=(Get-Location).Path
if (!(Test-Path -LiteralPath (Join-Path $repo 'dotnet') -PathType Container)) { throw 'Run from checkout root' }
$base=$env:CODEX_ARTIFACT_ROOT
if (!$base) { $base='E:\Artifacts' }
if (!(Test-Path -LiteralPath $base -PathType Container)) { throw 'Missing artifact root' }
$out=Join-Path $base ('Spice-semantics-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $out | Out-Null
$env:TEMP=$out; $env:TMP=$out; $env:CODEX_ARTIFACT_ROOT=$out
$jar=Join-Path $repo '.tools/antlr/antlr-4.13.1-complete.jar'
if (!(Test-Path -LiteralPath $jar -PathType Leaf)) {
  ./scripts/Initialize-SyntaxParser.ps1 -OutputDirectory $out
  $jar=Join-Path $out 'antlr/antlr-4.13.1-complete.jar'
}
dotnet test dotnet/Simulator.Syntax.Tests --artifacts-path "$out/build" "-p:GrammarOutput=$out/grammar" "-p:AntlrJar=$jar" --collect 'XPlat Code Coverage' --results-directory "$out/coverage"
./scripts/Run-SemanticMutations.ps1 -Stage Parameters -OutputDirectory "$out/mutations" -AntlrJar $jar
# -Stage Binding and -Stage Resources are separate profiles; -MutationPatterns
# supports Stryker's native source-span filters for exact follow-up selections.
./scripts/Run-SyntaxCorpus.ps1 -CollectSemantics -RequireValidSamples -Output "$out/corpus.json" -ArtifactsPath "$out/corpus-build" -AntlrJar $jar -Workers 4 -WorkerSeconds 90
```

On another platform supply an existing native artifact root instead of E:.
The default corpus observer never loads resources and reports reference/root-parameter
stages separately; include/library qualification uses isolated filesystem and
named oracle fixtures. Source-only header observations do not establish active
hierarchy, evaluated model parameters, bridge support or numerical oracle success.
Raw artifacts stay local; bounded verification and mutation dispositions are
recorded in the companion evidence. SEM-BIND-001, SEM-PARAM-001 and
SEM-RESOURCE-001 remain Draft, with explicit limits rather than broad closure.

## From syntax back to simulation

The new `dotnet/Simulator.Frontend` adapter references Syntax and Client; neither
underlying library depends on the adapter. Explicitly resolve resources, parse
with declarations, lower with `OperatingPointBridge.Lower`, check `Success`, then
send the typed `Request` through `SimulationClient.RunAsync`. No ngspice process
is required in the application path; it is an independent qualification oracle.
`RootNodes` maps user signals to wire names; `Origins` retains instance paths and
expanded-source ranges. Keep the resolver's source mappings for physical files.

This first text-to-request profile covers constant R/V/I DC operating points,
parameter scopes/overrides and hierarchical port binding. It atomically refuses
unsupported active analyses/devices/control commands, invalid scalars, recursion
and budget exhaustion. Only literal `0` is ground. Declaration uncertainty is a
global prerequisite even in unused definitions. Default limits: depth 32, 128
frames including the root, 1024 elements and 128 nodes. Wire backend keys are
`lu`, `qr`, `faer`; `faer_sparse_lu` is a provider manifest name, not a request key.

For reproducible Windows numerical qualification, use an existing native Rust
executable and ngspice installation with `scripts/QualifyOperatingPointBridge.cs`.
Arguments are checkout, Rust executable, ngspice executable, fresh output path.
Use `--artifacts-path`, `-p:GrammarOutput`, and `-p:AntlrJar` as in the commands
above so generated/build artifacts remain on the selected artifact volume.
Four named fixtures compare all three CPU backends with independent structured
requests, analytical values and ngspice. This is not model/AC/transient bridge
qualification, even though the existing engine has broader supported APIs.

For resource-backed corpus observation add `-ResourceRoots @($corpus,$repo)` to
`Run-SyntaxCorpus.ps1`. This opt-in profile records separate resolution/expanded
semantics and file path, selected section and SHA256 provenance. It never lowers
or simulates the corpus. Refused resources, expanded syntax failures, unresolved
bindings and simulation gaps must remain separate categories. The default syntax
acceptance gate does not certify expanded semantics or numerical behavior.
Resource-policy refinement (2026-11-01): `ResourcePolicy.ParsingLimits` optionally
selects recognition limits for every loaded file, including fragment-prefix
characters/tokens. Defaults remain the existing interactive profile. Corpus
resource/expanded recognition now inherits the campaign's declared parser limits;
file/byte/depth/allowed-root and strict UTF-8 limits remain separate.
`.csparam` alone no longer blocks unconditional loading, but remains unevaluated
and semantically deferred; actual conditional-resource selection remains refused.
`ResourceDiagnostic.SyntaxDiagnostics` supplies up to three physical parser
reasons for grammar/budget refusals without exposing a partial successful graph.

## Selected static DC execution

Use `OperatingPointSelection.Select(parsed, backend: "faer")` for an explicitly
selected linear static operating point. Inspect `Success`, then `Prepared.Request`
and `Omissions`. The strict `OperatingPointBridge.Lower` behavior is unchanged.
R/C/L and independent constant V/I are supported; original control programs and
non-selected analyses/observations are listed as unexecuted. Unknown physical
settings, source waveforms/AC headers and model devices remain refused. This
selected result is not a full run of the original analysis program.

The opt-in `scripts/Run-FrontendCorpus.cs` file app accepts existing checkout,
corpus, Rust executable, ngspice executable, prior syntax inventory JSON, fresh
output directory, optional `selected-linear-op`/`strict-rvi-op`, and optional
admission-budget seconds (1..21600, default1800). Set TEMP/TMP and all artifacts
paths to a checked unique E: directory on this host. Input control scripts never
execute. Raw expanded/projected sources, requests, responses and native logs stay
local. Successful solves, native agreement and unavailable observables are
separate outcomes; repeated backends do not inflate file counts.
