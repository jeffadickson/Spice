# Piecewise-linear RMS stability design

Authorized scope: investigate survivor causes and fix genuine unmutated bugs.
The existing RMS measurement squares physical sample values before averaging.
That can overflow for large finite amplitudes and underflow to a false zero
for small finite amplitudes. The target is the existing piecewise-linear
interpolant, not a change to integration, waveform samples or signal units.

Use standard Rust binary64 `hypot` and `midpoint` rather than implement a norm
or add a numerical dependency. Primary reference: the installed Rust1.99.1
standard-library documentation and
https://doc.rust-lang.org/std/primitive.f64.html#method.hypot.
The routine avoids unnecessary intermediate overflow/underflow. Arbitrary
precision would add a dependency and conversion policy for a bounded binary64
measurement fix; keeping raw squared values would preserve the known failure.

For segment endpoints a,b, the exact average square is
`midpoint(a,b)^2 + ((a-b)/(2*sqrt(3)))^2`. Normalize all endpoints by their
maximum absolute value first. Use `hypot` for that segment norm and for the
norm of segment contributions weighted by `sqrt(dt)/sqrt(window_duration)`.
Finally restore signal scale. Skip zero-duration segments before arithmetic.
The RMS of a linear interpolant cannot exceed its largest absolute endpoint;
cap the normalized accumulated norm at1 to preserve that physical bound against
roundoff. Check finite data, positive duration and nondecreasing segment times
before accepting the result; never let a NaN be masked by a minimum operation.
If all clipped endpoint values are exactly equal, return their absolute level
after checking segment ordering. This preserves the constant identity exactly
instead of introducing time-weighting roundoff into tight downstream assertions.

The real-valued convex bound is a possible small formal-proof candidate, but it
does not prove binary64 behavior. No Dafny or global floating-point proof is
claimed. Extreme time-axis ratios remain a numerical qualification limitation;
regressions cover ordinary translated windows and large, tiny, zero and selected
subnormal amplitudes. Preserve explicit unavailable outcomes on actual failure.

End-to-end path: Manifest recorded samples -> experiment::evaluate -> private
window calculation/RMS -> typed measurement value/status/unit -> assertions.
Units remain the signal unit for RMS; no schema, provider, FFI, clock, concurrency
or cache ownership changes are needed. Synthetic test manifests are marked as
measurement fixtures, never represented as solver output.

Verification must first reproduce the unmutated amplitude failures, then run
analytic window/property cases, malformed specification cases, mapped affected
native mutants, full regression, focused coverage, StrictDoc and Intel gates.
Frozen source/config/results remain immutable. New-source replay mappings and
reviewed zero-duration endpoint equivalents must remain distinct from kills.
