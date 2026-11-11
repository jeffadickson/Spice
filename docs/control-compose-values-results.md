The bounded reporting interpreter now supports `compose NAME values EXPR...`,
finite real `ln`, `log`, `log10`, and scalar zero-based vector indexing. It uses
the existing ANTLR grammar and evaluator. Sweep composition, complex results,
index slices and inter-analysis control remain outside this profile.

RSpice's pinned `engine/control/presentation/compose.rs` requires one element
per value expression. Source inspection guided the scalar admission rule;
RSpice itself was not executed for this change. The independent ngspice-47+
fixture confirmed that `values 1 -2 3` produces `[-1,3]` and that both `ln` and
`log` use natural logarithms. The original log regression supplies independent
gold values for the numerical assertions. See the new
[example](../examples/compose-values.cir) and StrictDoc requirements in
`control-compose-values.sdoc`.

The public-client qualification fixture executes one original OP on our native
engine and compares all two available root voltage/source-current observables
against ngspice. Its complete reporting program is also run: inspect the
actual `control-report.json` exit code and diagnostics, rather than inferring
assertion success from the row's numerical-analysis status. The native full
fixture separately exits zero. The final reporting result has zero relative
logarithm and subtraction errors and exits zero without diagnostics. Physical
comparison bounds remain 1e-6 V / 1e-9 A plus 1e-4 relative; reporting assertions
retain their independent 1e-15 bound.

Earlier reporting attempts exposed an unspaced subtraction ambiguity. The
first numerical OP comparison passed while reporting failed on `gold-1`.
An atom-level fallback then produced wrong division precedence and was
rejected. The retained fix resolves bounded arithmetic tokens before ANTLR
expression parsing, preserving exact vector names and probe terminal names.
Failed reports and the explicit precedence regression remain in raw evidence.

Raw output is under `E:/Artifacts/Spice-compose-68c67e6`. The final measured
counts, source hashes, commands and coverage scopes are recorded in
`evidence/control-compose-values.json`. A deliberate log10-to-natural-log
mutation was detected and the source restored. The composition property uses
128 cases with replay `385,20261111`. Negative cases cover nonfinite logs,
nonscalar inputs, missing values, forbidden sweep forms, invalid indices and
typed-command value-budget bypasses. No full Stryker campaign or long fuzz run
is claimed.

All bulk build, coverage, temporary and oracle files remained on E:. Initial
850 MiB admission checks were refused when RAM was unavailable. Some later
combined runs were terminated by the 512 MiB watchdog and are incomplete
evidence. Serial direct VSTest execution removes MSBuild's concurrent footprint;
the opt-in settings follow [xUnit's documented VSTest configuration](https://xunit.net/docs/config-runsettings).
The watchdog retains explicit aggregate limits, 450 MiB startup headroom and
whole-tree termination. An initial direct VSTest attempt could not discover
Coverlet; it supplies test evidence only. Covered runs explicitly provide the
installed collector path. The metric scopes include generated code and differ
between syntax and frontend suites; they are not whole-project coverage or
comparable scores for those two suites.

The unchanged original `regression/misc/log-functions-1.cir` still refuses
physical preparation at `non_affine_behavior`. Its `compose` refusal is gone,
but this checkpoint adds zero completed-and-agreeing original corpus decks.
The campaign's cumulative count remains one. Next connect nonlinear behavioral
sources through the existing bounded expression/model contracts; do not replace
the original deck with this qualification fixture or count syntax admission as
simulation support. Windows CPU evidence here does not qualify Linux, GPU,
general model families or full SPICE compatibility.
