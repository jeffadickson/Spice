# Independent SpiceSharp third-reference checkpoint

The original928-input inventory's50 native-compared selected OP files were
submitted to an independent SpiceSharp parser/model/solver operation:

| Result | Files |
|---|---:|
| OP and selected AC agree with ngspice and both Rust providers |12|
| OP agrees; no qualified input AC cohort |24|
| Reference unavailable/unsupported |13|
| Actual numerical/semantic discrepancy |1|
| Outside this OP-compared cohort |878|

This is36 agreeing OP files, not50 third-reference passes. The one disagreement
is `compat/ngspice/semantic-parameters.cir`: `2^3^2` evaluates to64 in ngspice and
Rust but512 in SpiceSharp. Treat it as dialect/associativity evidence, not proof
that any one simulator is universally correct. Nine unavailable files use DC
source forms rejected by the reference parser; two use resistor forms it rejects;
one comma-separated PWL is rejected; one scope-processing worker fails without
a completed result. Details and exact hashes are in
`evidence/third-reference-results.json`; raw per-job inputs/results/logs remain E:.

Separately, the original named two-file bug883 circuit now agrees across all
three simulators in selected OP and selected1/100/1000Hz AC. This owned fixture
is outside the frozen928 inventory and does not inflate those counts.

The isolated worker pins SpiceSharp3.2.3, SpiceSharp-Parser3.4.1 and transitive
SpiceSharpBehavioral3.2.0. Stock switch evaluation throws an illegal short IL
branch exception. A separately labeled reference rebuild changes16 short branch
opcodes to long forms in pinned MIT source, without changing numerical formulas.
The setup script verifies archive/file preimages and records license, patched
source and DLL hashes. No upstream DLL/source is committed, global package cache
is untouched, and no production dependency was added. This is a patched-reference
result, not a claim that the stock package ran the switch successfully.
Primary source: [Behavioral pinned commit](https://github.com/SpiceSharp/SpiceSharpBehavioral/tree/37cefa9afe9b3cbc19615ba29026b0ad1b2d2003).

The raw selected projection is independently reconstructed and verified before
execution. The third parser stalls on long runs of blank physical lines left by
omitted control blocks. Its separately hashed input removes blank lines after
retaining the first title line and all nonblank physical lines verbatim, including
continuation prefixes. Raw projection, normalized base and final analysis deck
hashes remain distinct. No physical/model card is rewritten to force agreement.

The coordinator verifies finite, complete, correctly named V/A vectors, exact OP
coordinates and AC frequency identity within four adjacent binary64 values.
SpiceSharp's angular-frequency conversion exports999.9999999999999 for1000Hz;
this narrow identity allowance does not change the1e-9V+1e-8relative observation
bound. Assembly hashes/flavor/patch hashes must stay constant across jobs.
Each child has30s supervision; timeouts, missing/unsupported output, numerical
mismatches, protocol failures and budget exhaustion remain different outcomes.

Verification: two integration tests cover independent1V/-1mA exports and ten
invalid/ambiguous job cases with empty failure samples. Frequency self-tests
exercise exact, one/four/five-step, nonfinite and wrong-coordinate boundaries.
Coverlet: worker88.57% lines/50.9% branches for one analytical OP job;
coordinator96.46% lines/64.23% branches over this corpus replay. Negative worker
jobs executed outside child instrumentation; upstream library internals and
Linux were not covered. StrictDoc parsed21 documents/340IDs/328relations.
Earlier stalled and overly strict frequency-identity attempts are retained as
aborted/failing evidence, not passes. No broad performance, full-SPICE, GPU or
production qualification follows from these comparisons.

Setup/run contracts are in `evaluation/SpiceSharp.ThirdReference/README.md`.
