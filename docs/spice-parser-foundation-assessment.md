# SPICE parser foundation assessment

The user correctly identified that incremental card-shape exceptions are not a
sound plan for a general SPICE front end. The current implementation is an
explicitly bounded importer; its numerical tests do not establish grammar
conformance. Further syntax/model expansion is held while the parser boundary is
replanned. No replacement parser or library has been implemented or adopted.

## Published primary references

- [Ngspice documentation](https://ngspice.sourceforge.io/docs.html) and
  [manual](https://ngspice.sourceforge.io/docs/ngspice-manual.pdf): target syntax,
  device/directive contracts, preprocessing and distinct expression languages.
  The project's local reference is SourceForge ngspice revision
  e8285746c73a8229cf922487c8ccfcf6bea61bce, executable ngspice-47+.
- [Sandia XDM HSPICE grammar](https://github.com/Xyce/XDM/blob/master/src/c_boost/xyce/HSPICEGrammar.hpp)
  and [PSpice grammar](https://github.com/Xyce/XDM/blob/master/src/c_boost/xyce/PSPICEGrammar.hpp):
  concrete Boost.Spirit productions for devices, directives, waveform syntax and
  expressions. They are dialect-specific, GPL-3.0-or-later source, not an
  authoritative ngspice grammar or permission to transplant code unreviewed.
- [KiCad SPICE grammar](https://docs.kicad.org/doxygen/spice__grammar_8h_source.html):
  PEGTL rules for lexical details, continuations, models and subcircuits.
  Its generic dot-line/unknown-line fallbacks mean recognition is not complete
  validation of device/directive syntax. Source header is GPL-3.0-or-later.
- [SpiceSharpParser](https://github.com/SpiceSharp/SpiceSharpParser): an MIT-licensed
  .NET implementation worth evaluating for reuse or independent differential
  tests. The documented parse-model/reader boundary is useful. Coupling to its
  runtime, actual ngspice coverage and dependency/license details require an
  integration assessment; README feature claims have not been independently
  executed here.
- Ngspice's `src/frontend/parse-bison.y` is an expression grammar, not a complete
  deck grammar. The actual frontend also uses preprocessing in `inpcom.c` and
  per-card/device parsing under `src/spicelib/parser`. A single `.y` file is not
  sufficient to reproduce the language. Source is available from the project's
  [official repository](https://sourceforge.net/p/ngspice/ngspice/ci/master/tree/src/).

No universal authoritative grammar for all SPICE dialects was established by this
review. Xyce's own [compatibility FAQ](https://xyce.sandia.gov/documentation-tutorials/frequently-asked-questions/)
explicitly distinguishes its basic SPICE3F5 syntax and simulator extensions.
We should pin our ngspice target rather than claim universal SPICE compatibility.

## Actual implementation gap

`src/netlist.rs` folds some continuations, repeatedly splits physical/logical
text, recognizes token counts and rewrites accepted forms into primitive circuit
text. Request-level analysis/source scans and circuit-level scans are separate.
`ngspice_resistor.rs` and bounded diode lowering resolve model physics before a
complete syntax representation exists. Model, grammar and execution failures are
therefore mixed. This is useful prototype lowering, not the architecture's
general ParsedDeck -> ElaboratedCircuit -> ExecutionPlan pipeline.

Examples: an NPN `.model` card can be syntactically valid while the numerical
BJT is unavailable. UIC and multiple analyses can likewise be parsed and retained
even when execution is unsupported. Their AST construction must not depend on
being able to simulate them. Includes need explicit resolution/resource policy;
control blocks must be represented without executing arbitrary commands.

## Corrective direction

1. Inventory the pinned ngspice syntax by lexical rules, statement productions,
   expression dialect and contextual/elaboration rules. Compare published
   grammars and maintained parser libraries before selecting a generator/library.
2. Build a source-located immutable syntax tree independently of solver/model
   support. Preserve model cards, analyses and hierarchy instead of rewriting
   them away. Add a separate bounded elaborator into the existing typed core.
3. Run a parser-only campaign on all 910 inventoried decks. Report actual syntax
   errors, grammar gaps, unresolved dependencies and model/analysis capability
   gaps separately. Parsing success must never become a numerical pass.
4. Drive production-level conformance, malformed-input, seeded property/fuzz,
   mutation and source-span tests from the grammar. Keep end-to-end ngspice
   numerical comparisons as an independent qualification layer.

Draft requirements NGC-PARSE-001 through NGC-PARSE-004 record this gap. They are
not implemented or accepted. The already-started sheet-resistor changes remain
local and uncommitted: 84 focused Rust tests and clippy passed, but new managed
oracles, mutations, coverage and corpus qualification have not run. Do not publish
that feature as qualified while this architectural correction is unresolved.
