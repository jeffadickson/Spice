Qualification and replay
========================

Proptest 1.9.0 provides bounded Rust generation, shrinking and persisted regression
seeds. PROPTEST_CASES controls requested cases per property and PROPTEST_RNG_SEED
selects a replay seed. The ordinary suite uses a fixed default seed. The overnight
supervisor discovers the property inventory and counts requested successful cases
only after an entire batch passes; persisted replays and shrinking are excluded.

FsCheck.Xunit 3.5.0 generates managed signal-selection/permutation cases with
Replay 362,20261027. The synthetic subprocess fixture challenges envelopes,
exit status, cancellation and UTF-8 byte limits. It is distinct from the real
engine conformance test and is not numerical evidence.

Cargo-mutants 27.1.0 and Stryker.NET 5.0.0 are the primary mutation engines.
Survivors, timeouts, uncovered and compile-error mutants remain separate from
test-detected kills. Supplemental hand-selected mutations require explicit opt-in.
Short deadlines may produce partial or unavailable outcomes.

Coverlet 10.1.0 and ReportGenerator 5.5.11 measure the owned managed client.
Rust LLVM line coverage excludes test/vendor internals; branch metrics remain
unavailable in the qualified instrumentation. Always retain source/tool identity,
exact coverage scope, warnings, seeds and unresolved counterexamples.

Independent physical checks
---------------------------

Linear AC manifests independently check node current, voltage-defined branches
and absorbed complex power using peak phasors, ``S=V*conj(I)/2``. Fixed configured
BE includes discrete numerical dissipation; linear trapezoidal uses midpoint
voltage/current energy identities. Checks precede derived signal expansion.
Nonlinear AC without retained bias and adaptive intervals without accepted
midpoint history report ``not_checked``. Physical consistency never closes
global waveform accuracy. Failures prevent publication of a successful manifest.
The existing structured tracing framework writes diagnostics to stderr while
stdout remains the JSON protocol. Requirements AUD-AC-001 and AUD-CONFIG-001
and evidence/five-area-audits.json define the finite qualification scope.

Use ``scripts/ValidateRequirements.cs -- --python <StrictDoc Python> --report
<existing-parent>/requirements.json`` from the checkout. Real StrictDoc parses
the grammar; the C# supervisor also rejects duplicate stable IDs and unresolved
parents across documents and checks unchanged source hashes. Its ``--self-test``
exercises positive, duplicate and missing-parent cases. This gate validates
structure and identity, not behavioral requirement closure.

Parser-only qualification
-------------------------

The separate ``Simulator.Syntax`` frontend generates its lexer and parser with
ANTLR 4.13.1. It references no numerical simulator. ``Simulator.Syntax.Corpus``
attempts every selected upstream inventory row and tracked repository SPICE
sample in isolated workers. Reports distinguish structural acceptance, grammar
gaps, syntax rejection, encoding/resource rejection, worker failure and timeout.
Control blocks and include paths are preserved without executing or loading them.

Run ``scripts/Initialize-SyntaxParser.ps1`` once, then
``scripts/Run-SyntaxCorpus.ps1``. Java 11+ is a regeneration dependency; .NET 10
executes the managed frontend. Generated code is incremental and remains under
``obj``. The production bounded Rust importer is not replaced by this slice.
Canonical requirements are ``docs/parser-only.sdoc`` (PRS IDs, including the
XSPICE, lexical, profile, prediction and acceptance-gate contracts);
the three-reference comparison, grammar inventory and actual results are in
``docs/parser-only-plan.md``, ``docs/parser-grammar-inventory.md`` and
``docs/parser-only-results.md``. A parser rejection is not independent evidence
that an upstream example is malformed.

Campaign bounds are explicit: ``-WorkerSeconds``, ``-Minutes``, ``-Workers``,
``-MaxInputBytes`` and ``-MaxTokens``. ``-LegacyBytes`` opts into lossless Latin-1
byte mapping after a strict UTF-8 decoding failure; reports identify that choice.
Default library limits remain smaller than the campaign profile.
Add ``-RequireValidSamples`` to run the explicit legacy subset replay and the
acceptance gate automatically, reusing the same binary set. It cannot be
combined with ``-LegacyBytes`` because the primary must exercise strict UTF-8.
``Test.ps1 -SyntaxOnly -SyntaxCorpus`` enables this gate with a 90-second
per-input budget; the ordinary report-only corpus command remains available.

``scripts/ValidateSyntaxAcceptance.cs`` takes a completed primary report,
``compat/ngspice/parser-input-adjudications.json``, authoritative inventory,
repository root, output path and optional completed replay reports. It checks
inventory membership, input hashes, source and binary identity; unexplained
failures remain outstanding. ``scripts/Test-SyntaxAcceptance.cs`` exercises
synthetic corruption scenarios; it is not sample acceptance evidence. Actual
commands and results are recorded in ``docs/parser-gap-results.md``.

Corpus capability inventory
---------------------------

``scripts/Run-CapabilityInventory.ps1`` reuses the bounded generated parser,
strict primary campaign, adjudicated legacy replay and acceptance gate. Supply
an existing writable ``CODEX_ARTIFACT_ROOT`` or an explicit fresh
``-OutputDirectory`` with an existing parent. ``-CorpusRoot`` overrides the
inventory's machine-local corpus location. Raw reports and build outputs stay
in that output directory; JSON, Markdown and SVG summaries are emitted there.
The script does not publish issues or run simulations.

``DeckParser.Parse(..., collectFeatures: true)`` exposes source-located immutable
observations only for accepted input. Quoted text and control-command bodies
remain opaque. Includes are counted without being loaded. Nested declarations
and unused model cards are included, so observation is not elaboration.
``CapabilityInventory`` counts occurrences and distinct decks separately;
model LEVEL values retain literal spelling, with explicit unspecified,
expression and multiple-declaration profiles. No default LEVEL is inferred.

``compat/ngspice/capability-support.json`` pins conservative implementation
assessments to a committed source revision. Missing assessments remain
unassessed; family-level partial support never implies parameter parity.
Rankings overlap and measure reach rather than circuits unlocked. The SVG
starts a new assessment baseline, separate from numerical corpus burndowns.
Canonical requirements are ``docs/capability-inventory.sdoc`` (CAP-001/002).

Declaration catalog
-------------------

``scripts/Run-DeclarationCatalog.ps1`` reuses the syntax primary/legacy gate,
then runs ``ValidateDeclarationCatalog.cs`` against the exact report digests.
It requires catalog metadata for every accepted required input, coherent
counters and zero self-lookup inconsistencies. Ambiguous definitions remain
explicit catalog outcomes; they are not silently chosen or called invalid
ngspice circuits. Synthetic corruption checks live in
``scripts/TestDeclarationCatalog.cs``; actual sample counts are separate.

``DeckParser.Parse(..., collectDeclarations: true)`` exports immutable lexical
scopes, named model/subcircuit declarations and ordered parameter syntax.
``DeclarationCatalog.Lookup`` and ``LookupParameter`` preserve uncertainty
from unresolved resources, conditions and opaque targets. They do not evaluate
expressions, bind instance terminals, expand subcircuits or execute commands.
``DeclarationSummary.From`` checks catalog index consistency using grouped
candidate sets, including duplicate-heavy fixtures.

The tested source example is ``examples/DeclarationCatalog.cs``. Canonical
requirements are ``docs/declaration-catalog.sdoc`` (SEM-CAT-001/002); the delivery
sequence and limits are in ``docs/semantic-frontend-plan.md`` and
``docs/declaration-catalog.md``. This is not numerical qualification.

Generated frontend simulation verification
------------------------------------------

``Simulator.Frontend.Tests`` exercises atomic failures, supported source headers,
port/private-node isolation, inclusive expansion budgets and a 64-case FsCheck
property (replay ``901,20261101``). ``Run-SemanticMutations.ps1 -Stage Bridge``
uses the established Stryker.NET package against the adapter in an isolated
artifact snapshot. Generated parser and Rust mutation coverage are separate.
``examples/FrontendOperatingPoint.cs`` demonstrates explicit resource access,
lowering, typed engine execution and root-signal mapping.

``QualifyOperatingPointBridge.cs`` runs four named constant-DC fixtures through
three registered CPU backend keys and compares them with separate typed requests,
analytical values and native ngspice. It records hashes and raw outputs locally.
These fixtures do not establish arbitrary model, AC/transient or corpus numerical
conformance. Corpus resource rebinding is an opt-in observation stage with no
simulation, and its default syntax gate does not certify resource success.

Coverage caveat for this checkpoint: Coverlet 10.1.0 omits captured local functions
from raw Cobertura, including bridge ``Expand``/``Node`` and ResourceResolver
internals. Reported percentages describe the collector's instrumented subset;
they are not complete handwritten or core-lowering coverage. Closure-inclusive
measurement remains open. Keep the separate executed mutation/property/oracle
evidence and do not promote draft requirements based on the partial denominator.

November 3 Rust survivor replay
------------------------------

``tests/overnight_survivors_20261103.rs`` checks artifact ID byte boundaries,
nonzero common-mode differential switch controls and the direct audit's inclusive
1 S Gmin limit. Its 128-case midpoint property pins seed ``2026110303``.
``docs/overnight-survivors-20261103.sdoc`` records the three draft contracts;
``evidence/rust-survivors-20261103.md`` records the bounded mutation/coverage
scope and reproduction commands. The switch reference independently derives the
midpoint resistance, while the audit still shares the existing switch model law.

Newton globalization follow-up
------------------------------

The November 3 short changing-seed preflight exposed a reproducible switch-divider
Newton failure on both CPU providers. ``tests/switch_stagnation_20261103.rs``
retains the minimized analytical circuit; the original property generator and
assertions remain unchanged. ``docs/newton-merit-20261103.sdoc`` defines frozen
physical row references for backtracking, separate from strict final convergence
bounds. ``evidence/newton-merit-20261103.md`` records RED/GREEN, pending-seed replay,
mutation/coverage evidence and primary numerical references.
