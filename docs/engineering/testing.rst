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
