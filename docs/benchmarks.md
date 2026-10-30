# Paired ngspice comparison

The comparison runner executes owned resistor/DC, complex RC/AC and RC/configured
transient deck/request pairs from `compat/benchmarks/suite.json`. Later additions
are explicit inventory entries. Each row names the observable, units, sample
coordinates, absolute/relative tolerance and integration/model policy. The tool
checks actual provider identity, so request aliases cannot mislabel a different
backend. AC values are peak complex phasors; transient comparison uses declared
piecewise linear interpolation without extrapolation. OP coordinate zero is an
index, not a time or frequency.

Run from an existing checked-out root with the pinned SDK/native host tools:

```powershell
if (-not (Test-Path -LiteralPath scripts -PathType Container)) { throw 'Wrong checkout' }
$ngspice = (Get-Command ngspice -ErrorAction Stop).Source
if (-not (Test-Path -LiteralPath $ngspice -PathType Leaf)) { throw 'ngspice missing' }
./scripts/Run-ComparisonBenchmark.ps1 -NgspicePath $ngspice
```

An explicit executable path may replace PATH discovery. Defaults are one warmup,
three measured repetitions and a 300-second workload budget. `-Warmups`,
`-Iterations` and `-BudgetSeconds` are bounded configurable parameters. The wrapper
builds the Rust release engine and the C# NativeAOT executable incrementally.
`-SkipEngineBuild -EnginePath <existing executable>` reuses an explicitly selected
binary; its hash, not a guessed build mode, identifies it. `-Managed` explicitly
uses the managed helper when native compilation is unsupported. Build/metadata
setup precedes the workload clock and is outside child timing intervals.

The helper prints a new report path under ignored `build/benchmarks/<run>/`.
`-Report <new.json>` selects a new destination whose parent already exists. Existing
reports are never overwritten. Raw native stdout/stderr and ngspice logs remain
local. A bounded report may be copied into evidence after review; this command
does not publish result branches or dispatch CI.

## Interpretation

- Child intervals include spawn, stdin, setup/solve/serialization, pipe drain and
  exit. Parent numerical comparison and artifact writes are outside them. Warmups
  are retained but excluded from summaries; tool order rotates across repetitions.
- A case needs every selected numerical comparison to pass before its median,
  minimum and maximum become eligible. Failed setup, provenance or deadline
  invalidates all existing timing summaries. Raw failed observations remain data.
- [Stopwatch](https://learn.microsoft.com/en-us/dotnet/api/system.diagnostics.stopwatch?view=net-10.0)
  supplies the monotonic timer; the report records its frequency/resolution.
  [PeakWorkingSet64](https://learn.microsoft.com/en-us/dotnet/api/system.diagnostics.process.peakworkingset64?view=net-10.0)
  is the OS peak of shared and private resident pages, not private allocations.
  Unsupported/exited-process statistics are null with a reason, never invented zero.
- Compatible ngspice work counters are not inferred from printed rows. Absent
  engine counters remain null; reported Newton iterations concern the final stage,
  while reported linear-solve counts include the engine's documented work scope.
- Whole-program output formats and transient step controllers differ. These small
  fixtures cannot establish a pure numerical-kernel speedup, large-circuit scaling,
  statistical confidence interval, global waveform bound or full SPICE parity.
  Machine load/affinity is not controlled; use raw repetitions and explicit scope.

## Dependencies and verification

The maintained BCL is sufficient for external-process orchestration and JSON;
custom code handles paired numerical semantics and provenance. This is not an
in-process microbenchmark requiring BenchmarkDotNet. No new runtime package was
added. Source-generated serialization permits actual NativeAOT execution.
ngspice is the independent executable reference; its
[batch/.print protocol](https://ngspice.sourceforge.io/docs/ngspice-manual.pdf)
is parsed only for the exact named table. Executable/source bytes are hashed and
rechecked; the installed dynamic-library/OS image is not made immutable.

Run `dotnet test dotnet/Simulator.Benchmarks.Tests/Simulator.Benchmarks.Tests.csproj`
for malformed tables, signed complex values, exact/interpolated axes, no
extrapolation, suite/resource bounds, UTF-8/output bounds, cancellation and child
reaping. FsCheck has 128 cases at replay `372,20261031`. Native Stryker configuration
in that test directory selects Tables/Suites only; process/CLI/runner mutation
coverage is not implied by that scope. Requirements and actual evidence are in
`docs/consistency.sdoc` (BENCH-001) and `evidence/five-area-benchmark.json`.

The suite also includes signed 30 V coupled Shockley clamps; see [difficult-circuits.md](difficult-circuits.md) for the independent continuation oracle and its failed tighter-policy boundary. The five paired decks are owned fixtures, not a broad SPICE corpus.
