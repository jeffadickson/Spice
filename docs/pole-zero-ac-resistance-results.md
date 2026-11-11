The bounded PZ analyzer now admits validated requested passive variants,
substitutes AC-only resistor values in its local transfer view, and computes a
loaded RC pole as -(Gseries+Gshunt)/C. Its retained circuit preparation still
contains the original DC resistor values. The qualification coordinator consumes
that preparation rather than falling back to strict selected OP admission.
The legacy selected OP API remains unchanged.

This extends the existing bounded analytical frontend PZ implementation. It is
not a new Rust generalized eigenvalue solver. Extra loading and unsupported
transfer topologies remain explicit refusals. Source inspection of pinned
RSpice `crates/rspice-core/src/circuit/storage/passive.rs` (revision
29c28a022625fd7cc2c56ead1cce2d3066663074, lines 335–337 and 420–423) confirms
separate DC and small-signal conductance storage for AC/PZ/noise. No new library
is needed; the existing generated parser and requested resistor adapter are reused.

The authored example `examples/loaded-rc-pz.cir` executes its one original root
PZ request through the existing public C# corpus coordinator. Its one finite
pole is -500000 inverse seconds, exactly equal to ngspice-47+ RAW output; the
finite zero set is empty. The existing comparison retains 1e-7 inverse-seconds
absolute and 1e-9 relative bounds, complete root identity/count checks, and a
refusal for an empty comparison. Both source identity and final provenance
checks pass. This authored fixture is not counted as a newly working original
RSpice deck.

The unchanged original `regression/pz/ac-resistance.cir` is replayed separately
in the same inventory. It remains `requested_preparation_refused` with no
executed analyses. Ngspice completes its OP, AC and PZ assertions, but our full
script still requires ordered analysis-phase execution, complex reporting and
control PZ dispatch. Its secondary resistor diagnostic is produced by strict
fallback circuit preparation after control admission fails. It does not prove
that requested AC resistor support is absent. No analyses are omitted or
comparison tolerances weakened to count this original input as progress.

Verification: 33 focused PZ test cases pass, including 64 FsCheck property cases
with replay 5001,20261111. The final restored-source build has zero warnings and
errors; the managed frontend suite passes 828 tests with 40 explicit external-
prerequisite skips. A mutation multiplying the AC-only resistance by two is
caught: 29 focused cases pass and two pole checks fail (-375000 versus -500000).
The source is restored by hash, rebuilt, and all 828 tests rerun. Negative cases
reject unexpected loading without partial roots. No new long fuzz campaign,
Stryker campaign or Rust coverage run is claimed for this C#-only change.

Coverlet measured 20719/26408 lines and 5935/9468 branches across its managed
suite scope, including generated grammar code. The PZ analyzer class measured
96.50% line and 81.46% branch coverage. These are finite-scope measurements,
not proof of general SPICE compatibility. All 61 StrictDoc files parse with
the real reader; requirements remain explicitly bounded and Draft.

Pinned NuGet runtime/analyzer files were missing from the former C: cache.
The existing dependency lock files were restored unchanged into the run's E:
package directory using `dotnet restore --locked-mode --disable-parallel
--packages <run>/packages --artifacts-path <existing-E-build-cache>`. HTTP cache,
TEMP and TMP are on E:. No dependency version was added or upgraded. Final
builds use `--no-restore`, disabled shared compilation/build servers and one
MSBuild worker. Builds/tests run serially under a 640 MiB process-tree/aggregate
memory cap with 450 MiB OS headroom and a 64 MiB C: disk floor. The earlier
512 MiB guard kill is incomplete evidence, not a failed numerical test.

Bounded hashes, exact outcomes and comparison values are in
`evidence/pole-zero-ac-resistance.json`. Raw logs, the locked package cache,
Coverlet output and original/native replay remain local under
`E:/Artifacts/Spice-ac-resistance-a984f11`. This checkpoint adds zero agreeing
original decks; the campaign total stays two. Main remains untouched, with no
push or CI dispatch.

Final review added unloaded-RC and loaded-RC controlled-source refusal cases.
The runtime probe reproduced acceptance of an unmodeled device in the unloaded
case. Requiring all elements to belong to the recognized passive/source sets
fixes that gap; the final 828-case pass and reviewed coordinator replay include
this correction. The earlier 826-case run and mutation logs are historical
candidate evidence, retained separately from the final reviewed verification.
