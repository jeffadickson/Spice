# Two original requested programs — 2026-11-14

Two unchanged files from pinned RSpice `29c28a022625fd7cc2c56ead1cce2d3066663074`
now complete their requested analyses and agree with stock ngspice on LU and Faer.
Both full, unmodified stock programs also exit zero. No observer tolerance,
input, analysis, or startup sample was weakened or removed.

| Original path relative to the RSpice repository | Before | Qualified result |
| --- | --- | --- |
| `tests/ngspice/regression/misc/bugs-2.cir` | DC physics already prepared; saved source metadata/indexed reporting failed | All five DC points, ten physical signal-points, three original indexed-value checks, complete reporting exit zero; maximum physical error zero |
| `tests/xyce/Netlists/RESISTOR/resistor_neg.cir` | `invalid_resistance`; no requested execution | All six DC points and twelve voltage/current signal-points; maximum error zero |

The second is an existing Xyce regression retained by the pinned RSpice
repository. It is outside the historical 113-file `tests/ngspice` cohort.
These are two distinct original hashes, not two copies or authored replacements.
Already agreeing parser, library, pole-zero and resistance inputs found during
selection are not counted again.

Explicitly saved `@source[dc]` metadata now follows each immutable executed request,
including current sources, and repeats for that request's retained real samples.
Root save cards and control saves before dispatch select captures. A later save
cannot reconstruct old plot data, and `save all` does not imply device parameters.
Independent stock probes distinguish a five-point saved vector from an unsaved
read of the restored original scalar (7), rather than the last sweep setting (1).
A known
saved parameter remains an atomic vector inside a subsequent index expression.
Missing/type-changed/nonfinite settings refuse explicitly. This is terminal real
reporting support; it does not qualify arbitrary device parameters or expand
ordered multi-analysis parameter sampling.

A requested plain finite negative scalar resistance uses the existing native
self-controlled VCCS: `I(p→n) = (V(p)-V(n))/R`. Its original terminals and origin
remain intact. This is an active conductance, while positive resistors retain
their passive native representation. Zero, nonfinite negative reciprocal,
negative model/geometry/AC-only variants and legacy selected-OP admission keep
their explicit boundaries. No native model equation, solver, schema, dependency,
or capacity limit changed in this slice.

The separate authored `examples/negative-scalar-resistor.cir` agrees on both LU
and Faer for OP, five-point DC, three-frequency complex AC and the complete
51-sample transient: 120 signal-points per provider, maximum error zero.
It demonstrates the normal public entry point and is not a new original count.
Both originals agree on the final runtime-v4 serial LU and Faer replays, each with a valid source gate. The first combined LU
run timed out at bugs-2's first point under the unchanged 30-second point limit;
its other four points completed. That incomplete original is retained as a
failure in that run, with no claimed cause or fabricated missing response.

The frozen CPU engine is
`E:/Artifacts/Spice-two-originals-42e5356-20261114/cpu-engine.exe`, SHA256
`3ae9ac2c1552ad1240901cb3de9202da907db1bf9ef2a8a3d0e21790e41e2467`.
It is an existing parent-produced executable, not a fresh native build by this
slice. Managed source and assembly hashes, complete operation/projection records,
and successful immutable-source gates are retained in the raw replay manifests.
Current Windows `C:/dev` is a junction to `J:/dev`; qualification was compiled
and run from physical `J:/dev/spice-deck-next` without weakening link policy.

Verification includes 960 passing frontend tests and 40 explicit external-
prerequisite skips, two 128-case FsCheck properties with valid recorded replays,
and zero/nonfinite/model/type/ownership/index negative checks. The negative-R
RED property minimized to `NonNegativeInt 0`, equivalent to `R=-1 ohm`.
Two deliberate faults in disposable source snapshots were detected: discarding
the conductance sign fails three of fourteen focused cases; disabling atomic
saved-parameter AST lookup fails two. Both restored snapshots pass all fourteen.
Production files were never mutated by those campaigns.

Pinned Stryker.NET 5.0.0 was configured and attempted. An initial missing ANTLR
snapshot path was corrected; a later guarded attempt expired after 600.18 seconds
during its initial build, at 407,064,576 observed tree bytes. No Stryker mutant
was assessed, killed, or declared equivalent. The two executed manual faults
are separate evidence, not a broad Stryker score.

Coverlet collected line and branch metrics, including a fresh isolated build
and serialized full run. Its report omits large captured local-function bodies,
including the new negative-resistor lowering block; mapped coverage must not be
presented as complete coverage of that file. The supplemental collector outcome
is 10,225/12,882 lines and 7,012/10,649 branches for Frontend and Syntax, including captured bodies. This differs from the earlier transitive Coverlet scope; exact scopes are recorded in the evidence JSON. The F# diagnostic
reference script has four executed reference points and five failure/preservation
checks; instrumented line/branch coverage of that FSI script was not collected.

The JFET candidate remains uncounted. Its original 104-point DC sweep has four
stock-default current disagreements, maximum `3.09688701483415e-6 A`.
An independent scalar bisection reference derived from the pinned B=1 equations
and a separately tightened ngspice diagnostic agree with our four currents to
`2.60208521396521e-18 A`. This supports a reference-accuracy/profile diagnosis;
it does not prove a sole cause, qualify stock defaults, or justify changing the
model to follow the discrepancy. The reusable bounded reference is
`scripts/Jfet-LinearDcReference.fsx`. Res-array's transient comparison also remains
open. No failed or refined diagnostic became a passing original.

Requirements remain Draft in `source-dc-vector.sdoc` and
`negative-scalar-resistor.sdoc`. Exact commands, tool versions, hashes, counts,
coverage limits, seeds and raw paths are in `evidence/source-dc-vector.json`
and `evidence/negative-scalar-resistor.json`. Bulk artifacts remain on E:.
No main merge, push or CI dispatch was performed by this slice.
