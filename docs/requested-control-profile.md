# Bounded requested control analyses

The generated control-command entry prepares ordinary literal `op`, `dc`, `ac`
and `tran` commands inside one root `.control` block. Direct commands retain
source order and repeated identities. A finite `run` profile dispatches unique
outside analysis kinds in the independently observed native AC/DC/OP/TRAN order;
repeated `run` invocations receive separate identities. This remains individual
requested-analysis qualification, not complete control-program parity.

The existing ANTLR lexer/parser and typed public simulator client are reused.
There is no command text splitter or production interpreter dependency. Native
semantics were checked against the [official control-language tutorial](https://ngspice.sourceforge.io/ngspice-control-language-tutorial.html),
[user manual](https://ngspice.sourceforge.io/docs/ngspice-manual.pdf), and seven
owned native fixtures. Direct OP does not implicitly execute an outside TRAN.
Duplicate outside AC cards execute in reverse source order in ngspice47+; this
profile refuses duplicate kinds and mixed direct/run registration explicitly.

Only literal signed SI scalar command arguments are admitted. Variables,
expressions, separators, multiple/nested blocks, UIC, unsupported effects and
malformed commands refuse atomically. Limits are 256 commands per block, 4096
characters/256 tokens per command and 128 effective analyses. Exact reporting
settings `noaskquit`, `filetype=ascii`, and integer `numdgt=1..17` are recorded as
unexecuted metadata. Print/plot/meas/measure/save/gnuplot/echo/rusage and a final
quit are likewise explicit omissions. Original write/load/shell/alter/reset,
let/define/loops/noise and physics-changing settings never execute.

For each native comparison, generated ranges retain the original physical deck
and one original command, or original `run` plus one original outside card.
Other instructions are masked and recorded. Only an owned relative
`write native.raw all` and quit are inserted. Projection hash, original ranges,
instrumentation, program omissions and profile accompany the result. Blank RAW
point separators are accepted while shape/index/finite/extra-plot checks remain.

Owned direct and repeated-run decks agree for all 13 individual operations; a
state-changing deck is explicitly refused. A byte-identical copy of upstream
`examples/measure/simple-meas-tran.sp` prepares and executes its actual TRAN once,
but comparison remains a numerical disagreement: peak errors approximately
0.000397891 V at v(1), 0.000386772 V at v(2), and 3.97891e-9 A at i(vac1).
Tolerances are unchanged. These results do not close sine interpolation accuracy
or qualify measurement/report outputs. No new 928-input census was run here.

Verification commands, counts, hashes, coverage, property seed and remaining
limitations are retained in [bounded evidence](../evidence/requested-control-profile.json).
Native helpers remain managed .NET 10 file apps; Native AOT was not attempted.

The [owned example](../examples/requested-control.cir) demonstrates repeated OP and direct DC/AC/TRAN dispatch with explicit reporting omission.

Review correction: control-semantic tokens refuse dollar substitution embedded in
names (for example `V$curplot`), without changing ordinary deck name spelling.
Unary signs must touch their generated NUMBER token; `- 1` is refused. Native
ngspice accepts adjacent attached-sign DC arguments such as `1-1-1`, producing
`[1,0,-1]`; those remain supported. Reporting flag/value contexts must also be
unsigned, so `-noaskquit`, `filetype=-ascii`, and `numdgt=+7` refuse. Original
native failure witnesses retain constants RAW output to show why file existence
and a zero process exit do not establish successful requested analysis.
See [review evidence](../evidence/requested-control-admission-review.json).
