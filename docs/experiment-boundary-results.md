# Experiment integrity and RMS stability checkpoint

Base `3523ca4`. A genuine unmutated RMS defect was reproduced: the new focused
suite initially passed five tests and failed two. A constant1e154recording was
unavailable after intermediate overflow; constant1e-200collapsed to a false zero.
The before-source hash equals frozen experiment.rs, and before-fix logs remain
local. The fixed focused suite passes all seven tests.

RMS now keeps the same piecewise-linear meaning while normalizing amplitudes,
using standard-library midpoint/hypot for analytic segment norms, weighting by
square-root durations, and restoring signal scale. Zero-duration segments are
skipped. Exactly constant clipped values return their absolute level after
ordering checks. A finite-check-before-cap preserves the physical endpoint
bound against roundoff; it does not mask NaN. Solver equations, units, result
schema and assertion outcome policy are unchanged.

Tests cover inclusive256/512capacities and one-over limits, finite equal versus
inverted/nonfinite assertion bounds, outside-window exclusion and units,
constant maximum-finite/normal/subnormal amplitudes, a large triangular waveform
with RMS amplitude/sqrt(3), and unavailable data with indeterminate assertions.
Proptest ran64generated translated-window cases, seed2026111929. Synthetic
manifests explicitly identify no solver and are not presented as circuit output.

Mutation evidence is deliberately separated:

- A disposable3523ca4source archive, with experiment.rs and measurement.rs
  matching frozen bytes, ran only the triangular regression: baseline1passed,
  six tests filtered. Both original endpoint-inclusion mutants were caught:
  **2/2caught**, no invalid/timeouts. Other source belongs to the stated archive
  revision; this is not a whole frozen-cohort rerun.
- Current source replay selected11mapped originals plus7supplemental mutations
  of the new RMS implementation: **16caught,2missed**, no invalid/timeouts.
  Those are **9original-source counterparts caught +7supplemental caught**.
  The old RMS denominator site131:21 maps explicitly to new call argument127:69;
  the other ten original sites retain their names.
- The two current misses are reviewed physical equivalents only for the exact
  endpoint-inclusion changes on validated public recordings: a duplicate
  endpoint adds no minimum/maximum information, a zero-duration integral term,
  or RMS energy. Current RMS skips the zero duration before arithmetic. This
  does not claim bitwise equivalence, malformed private-input behavior, or
  equivalence in the old implementation (whose counterexamples were executed).

Both native stages cleaned the isolated owned replay package first and freshly
compiled the owned library. Current baseline passed all7focused tests. Every
original name/diff, replay mapping, archive/source/test/executable/tool identity,
seed and native outcome is retained. All11original cases have explicit evidence;
do not add the7supplemental kills to the frozen survivor count or rewrite scores.

Regression: **55unit+726integration passed;7ignored**. Instrumented focused7passed.
Regression/native environment seed2026111930; the property pins its own seed.
Scoped experiment line coverage **132/145(91.03%)** excludes tests/vendor; branch,
main and other entrypoint coverage remain unmeasured. Coverage is not a proof.
Actual Intel routine15tests and separate UHD630FP64/finite/shape/corruption probe
passed, driver31.0.101.2141, probe seed18117. Real StrictDoc parsed127documents/
667uniqueUIDs. Read-only simulated numerical/source review found no important
issue; ordering checks before constant return were incorporated before replay.

Five additional frozen compile-invalid logs were reviewed: generated Default
calls unsupported by Expr, WireExpr or BackendCapabilities (E0277). They remain
invalid, not product defects/test kills; no Default implementations were added.

Limits: no correctly-rounded/global RMS guarantee, extreme time-axis-ratio or
general interpolation qualification, new original-deck/ngspice execution,
Linux, Stryker, standalone fuzz or throughput claim. The private RMS helper
assumes its validated caller's clipped points partition the supplied duration.
Raw artifacts stay at E:/Artifacts/Spice-experiment-contracts-20261119T211131Z.
Bounded evidence: evidence/experiment-boundary-results.json. Design and
implementation plan are recorded alongside the four Draft StrictDoc contracts.
