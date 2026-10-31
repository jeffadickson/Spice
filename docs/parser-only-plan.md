# Parser-only front end and qualification plan

User direction: compare all three front ends, choose the generator, test every
sample for input acceptance before adding numerical behavior. This explicitly
authorizes the framework choice and implementation; the Rust core is retained.

## Decision and alternatives

Use ANTLR 4.13.1 generator and Antlr4.Runtime.Standard 4.13.1 (BSD-3-Clause).
It generates both C# lexer and parser from reviewable grammar files, supports
lexer modes, source positions, case-insensitive rules and declarative expression
precedence. Java is needed only to regenerate sources, not to run the parser.
Sly 3.8.1 (MIT) offers attractive attribute-based EBNF and no Java build step;
ANTLR's explicit lexical modes and separate grammar artifacts fit the title,
deck and command-language boundaries better for this implementation. This is an
engineering judgment, not a measured performance comparison. Superpower and
Parlot provide parser combinators, but put more of the grammar in executable C#
than the selected approach. No comparative speed claim is made.

Primary sources: [ANTLR](https://github.com/antlr/antlr4/tree/4.13.1),
[lexer modes](https://github.com/antlr/antlr4/blob/4.13.1/doc/lexer-rules.md),
[Sly](https://github.com/b3b00/csly),
[Superpower](https://github.com/datalust/superpower),
[Parlot](https://github.com/sebastienros/parlot).
NuGet versions and repository activity were inspected on 2026-10-31. Sly dev
was 1bac89b338b2ed059b6f02eb39835bba32847120 (2026-10-30); ANTLR master was
45be27daf036f79eced0e3e215fdfee78b4b38d2 (2026-10-30). Activity is not a
support guarantee. Pinned generator SHA-256:
bc13a9c57a8dd7d5196888211e5ede657cb64a3ce968608697e4f668251a8487.

## Three-reference comparison

| Concern | ngspice | XDM C++ | SpiceSharpParser C# | Consequence |
|---|---|---|---|---|
| Whole deck | Preprocessing plus card dispatch | Boost.Spirit dialect grammars | Published BNF plus handwritten LL(*) | Own explicit grammar, no copied parsing machine |
| Lexing | inpcom.c, token readers, compatibility modes | Whitespace/identifier/expression productions | Regex lexer and continuations | Generated lexer, preserve physical positions |
| Devices | inp2r/c/l/v/i/d/q/m etc.; model-aware interpretation | Named resistor, BJT, MOS, source and switch productions | Generic COMPONENT word plus PARAMETERS | Validate family structure, preserve model names; no model execution |
| Expressions | Different frontend/device expression contexts | Separate dialect expression grammars | Braces/quotes and parameter productions | Balanced groups first; expression validation scope explicit |
| Hierarchy | .subckt/.ends, includes and library preprocessing | Dialect subcircuit/include/library productions | SUBCKT plus optional ENDS name | Nesting is syntax; file resolution is separate |
| Control | Nutmeg command language | Translator-oriented directive subset | Generic dot control production | Preserve control block without running or claiming command validation |
| Evidence | Oracle execution does more than syntax | Translation is not ngspice conformance | 488/910 structural acceptances in prior trial | Independent parser-only campaign and honest validation levels |

Pinned ngspice e8285746c73a8229cf922487c8ccfcf6bea61bce:
`src/frontend/inpcom.c`, `src/frontend/parse-bison.y`,
`src/spicelib/parser/inp2r.c`, `inp2q.c`, `inp2dot.c` inspected locally.
The expression .y file is not the netlist grammar. R permits a scalar/model and
named qualifiers; Q node count can depend on the model. Such context-sensitive
resolution belongs after parsing.

XDM b6394a0275eb94f39b86584fa1e0fa9a3766ed23:
[`HSPICEGrammar.hpp`](https://github.com/Xyce/XDM/blob/b6394a0275eb94f39b86584fa1e0fa9a3766ed23/src/c_boost/xyce/HSPICEGrammar.hpp)
and sibling PSPICEGrammar.hpp. These are HSPICE/PSpice dialect references, not
normative ngspice syntax. GPL source is studied, not copied into this project.

SpiceSharpParser 8fc01560fe303eeebaeb5cbf5edfe77bf635e1c7:
[`SpiceGrammarBNF.txt`](https://github.com/SpiceSharp/SpiceSharpParser/blob/8fc01560fe303eeebaeb5cbf5edfe77bf635e1c7/src/SpiceSharpParser/Parsers/Netlist/Spice/SpiceGrammarBNF.txt),
`Internals/ParseTreeGenerator.cs` and lexer. The generic COMPONENT production
does not establish device arity correctness. MIT dependency trial results are
in spicesharp-evaluation.md. No production dependency on its simulator is added.

## Implementation sequence and boundaries

1. Add malformed and valid fixture tests before the implementation. Include
   unsupported numerical models, repeated analyses, hierarchy, includes and
   control commands that must have no side effects.
2. Generate a C# lexer/parser from independent declarative grammar files. Build
   immutable source-located statements without resolving files or models.
   Unknown extensions are grammar gaps, never silent success. Distinguish
   structurally accepted parameter tails from fully validated productions.
3. Add a bounded corpus CLI: every one of the 910 inventory rows plus every
   tracked repository SPICE sample, source/input/grammar hashes, counts and
   per-input diagnostics. Child-process timeouts, strict UTF-8, byte and nesting
   limits protect the campaign; unavailable inputs are not passing cases.
4. Run negative, seeded property/fuzz and relevant Stryker checks. Collect line
   and branch coverage. Generated recognition machinery and handwritten glue
   scopes must be distinguishable. Run StrictDoc and document actual limitations.
5. Review, commit only the parser slice, push without dispatching CI. Preserve
   the unrelated local sheet-resistor work.

The frontend does not replace production import yet. It does not invoke ngspice,
SpiceSharp, the Rust solver, a shell, include resolution or Nutmeg commands.
Acceptance is a grammar result, never a simulation-support declaration.

Independent review added two requirements to the implementation details: bound
recursive grammar forms as well as delimiters, and check executable hashes plus
the actual corpus Git revision. A generic ANTLR rule listener limits rule depth
to 512; pre-token guards bound groups, subcircuits and consecutive unary chains
to 128. These are explicit resource limits, not language-invalidity judgments.
Completion rechecks source/DLL hashes; intentional changed-binary CLI fixtures
must report `provenance_changed`. This detects ordinary concurrent changes, not
malicious transient replacement and restoration of bytes between checks.
