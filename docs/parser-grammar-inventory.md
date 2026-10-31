# Parser grammar inventory

This is the declared first grammar profile, not a claim that every ngspice
production is implemented. Reference revision/hash data is in
`evidence/parser-grammar-sources.json`. The framework decision and comparison
of the three implementations are in `parser-only-plan.md`.

| Production | Independent grammar rule | Current validation boundary |
|---|---|---|
| Title | lexer TITLE, TITLE_EOF | First physical line is always title in deck mode |
| Comments | COMMENT, INLINE_COMMENT | Whole-line asterisk and inline semicolon; dialect dollar rules remain a gap |
| Continuations | CONTINUATION hidden channel | Immediate newline-plus; offsets preserved; intervening comment continuation needs qualification |
| Scalars | NUMBER, value | Signs/exponent/engineering suffix are recognized, not converted or unit-checked |
| Parameters | assignment, call, group | Balanced groups, name=value, waveform call structure; allowed parameter sets/arity not checked |
| Braced expressions | expression | Unary/binary/comparison/Boolean/ternary/function syntax; dialect operator precedence not numerically qualified |
| Quoted expressions | QUOTED | Preserved opaque; expression inside quotes not validated |
| R/C/L | passive | Two nodes and nonempty value/model/parameter tail |
| V/I | source | Two nodes and source-clause structure; specific waveform argument counts not checked |
| D/Q/M/J/Z | diode, bjt, mos, jfet | Minimum terminals/model-shaped tail; model-dependent node disambiguation deferred |
| E/G/F/H | vcontrol, icontrol | Basic terminal/control structure; polynomial/table forms need detailed productions |
| S/W/K | switchCard, cswitch, coupling | Minimum terminals/control/model/coupling tail; value validity deferred |
| B/T/O | behavior, lineCard | Minimum node/tail structure; full behavioral and transmission-line dialect validation deferred |
| X and hierarchy | subcall, subcircuit | Subcircuit blocks nest and close; name matching/port binding deferred |
| Models | model | Name, type and balanced parameter tail; arbitrary model families remain syntactically representable |
| Analysis | tran, ac, dc, op | Separate analysis statements retained; no execution-support lookup |
| Parameters | paramCard | One or more assignments, balanced expression values |
| Includes | includeCard | Path syntax only; no filesystem access |
| Other named directives | extension + explicit StructuralDirectives set | Balanced structural tails only; not complete command-specific validation |
| Unknown directives/devices | gap classification | Never promoted to accepted syntax just because text was preserved |
| Nutmeg | control + COMMAND lexer mode | Block closure and source retention only; commands do not execute or receive syntax validation |

Full parameter validation, all ngspice dialect modes, expression languages,
array/vector/node-name spellings, conditional/library context, full XSPICE
card productions and preprocessor behavior remain concrete grammar backlog.
The parser must expose these limitations rather than hide them behind the
generic parameter productions. In particular, `accepted_structure` is a useful
first acceptance gate, not final syntax-conformance closure.

Corpus `syntax_rejected` means this grammar rejected the input. It does not
prove the upstream sample is malformed. Each rejection must be adjudicated as a
grammar gap, a non-deck asset, an invalid fixture or a resource issue before
closing a language requirement. Numerical corpus outcomes are a separate series.
