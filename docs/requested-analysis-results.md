Requested ordinary analyses
===========================

`RequestedAnalysis.Prepare` now uses generated ANTLR analysis contexts and the existing
parameter evaluator to prepare every supported root .op/.dc/.ac/.tran card in source
order, including repeats. `ExecuteAsync` calls the public one-shot SimulationClient;
every request starts fresh. The returned operation identity, range, original evaluated
arguments and SI coordinate unit preserve the input analysis provenance. Observation
and reporting instructions remain explicit unexecuted metadata.

Single-source DC is a sequence of fresh operating points, including descending and
non-unit steps. It has no continuation/hysteresis equivalence claim. AC uses the
requested lin/dec/oct grid and all prepared source phasors. Transient retains original
TSTEP-dependent waveform defaults while using explicit TMAX, or min(TSTEP,TSTOP/50),
as its backward-Euler computational ceiling. Computational sample grids differ from
ngspice's adaptive grids. Output resampling, nonzero TSTART, UIC, .IC, input controls,
nested analyses and two-source nested DC sweeps remain explicit refusals. Existing
circuit/device lowering limits still apply. This is not arbitrary control-program or
whole-corpus execution.

The requested native test uses the original nine cards (OP; ascending and descending
DC; LIN, DEC and OCT AC; repeated OP/AC/transient), executing each original directive
independently in native ngspice so its batch job reordering does not erase repeated
identities. The passive divider with a linear PWL source has an independent analytical
solution; transient comparison interpolates the native oracle at our computational
times. Original requested grids are compared directly for DC and AC. A separate
engine-failure test corrupts one resistor only in a prepared point to verify atomic
operation failure and execution of the later OP.

Oracle discrepancy: installed ngspice47+ (build 2026-10-24) emits one point for
`.ac lin 2 10 30`; our documented requested grid is [10,30]. This case is not counted
as vector parity. Evidence is `lin2.cir`, `lin2.raw` and `lin2.log` under the E: run.
The ngspice `acan.c` source sets zero delta when `numberSteps-1 <= 1`.
Primary references, inspected 2026-11-02:
- https://ngspice.sourceforge.io/docs/ngspice-manual.pdf (analyses and TSTEP/TMAX)
- https://raw.githubusercontent.com/ngspice/ngspice/master/src/spicelib/analysis/acan.c
  (AC grid behavior; mutable upstream source, local native evidence is authoritative
  for the exact recorded binary)

Reproduction
------------

Run from an existing checkout with .NET10.0.403, Java and the pinned ANTLR4.13.1 jar.
Check directories before use; choose a fresh output subtree per run. All build,
grammar, native oracle, coverage and mutation outputs go under E:/Artifacts (or the
configured artifact root). Set SPICE_CORPUS_ENGINE and SPICE_CORPUS_NGSPICE to existing
executables and SPICE_REQUESTED_ARTIFACTS to an existing writable artifact directory.

Before changing directories or writing outputs, use PowerShell checks:
`if (!(Test-Path -LiteralPath $checkout -PathType Container)) { throw 'Missing checkout' }; if (!(Test-Path -LiteralPath $artifactParent -PathType Container)) { throw 'Missing artifact parent' }; if (!(Test-Path -LiteralPath $run -PathType Container)) { New-Item -ItemType Directory -Path $run | Out-Null }`.
Check the existing executable and jar paths with `Test-Path -LiteralPath <path> -PathType Leaf`.

`dotnet test dotnet/Simulator.Frontend.Tests --artifacts-path <run>/build -p:GrammarOutput=<run>/grammar -p:AntlrJar=<existing-pinned-jar> --collect:"XPlat Code Coverage" --results-directory <run>/coverage --logger:"trx;LogFileName=frontend.trx"`

`dotnet run --file scripts/RequestedAnalysisCheck.cs --artifacts-path <run>/direct-build -p:GrammarOutput=<run>/direct-grammar -p:AntlrJar=<existing-pinned-jar>`

The qualification-only direct runner invokes all existing requested-analysis xUnit
assertions and FsCheck.Check.Method properties, including both native tests. It is
managed, not NativeAOT. It does not add product test callbacks. FsCheck uses 100 cases
per property with replay seeds 2357/2359 and gamma 20261103. The malformed-tail property
has a 256-character input bound. Both framework and direct evidence are retained,
because local VSTest discovery listed methods absent from executed TRX results. No
missing method is silently credited as executed. See evidence/requested-analysis.json
for actual run counts, scope, source hashes and limitations.

Final local evidence:172 passed, 9 skipped, 181 total in the frontend suite;39 requested
cases also pass through the independent invocation inventory. RequestedAnalysis.cs
has91/91 covered lines and117/124 covered branches. The bounded DC-grid Stryker replay
killed 14 of 18 tested mutants (4 survivors), with440 span-filter exclusions and62 compile
errors elsewhere in the frontend mutation assembly. The replay snapshot predates the
final rounded-count allocation guards; those guards have regression and coverage
evidence, without a mutation claim. The full evidence JSON distinguishes each scope.