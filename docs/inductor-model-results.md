# Linear inductor model checkpoint

This slice resolves requested constant inductor values before the existing solver
stamp. It adds model IND, named instance L/INDUCTANCE, model/instance temperature
coefficient precedence, TEMP/DTEMP, SCALE/M and unscaled winding IC. Model L is a
marker in default ngspice, not Xyce's multiplier. Geometry, nonlinear magnetic
models and runtime-dependent inductance remain outside this subset. Strict legacy
lowering retains its prior contract.

Observed regression progression: eight preparation failures and nine expected
refusals before implementation; 19 focused tests passed after implementation and
the first hierarchy/oracle additions. A subsequent 51-test affected pass included
64 seeded FsCheck cases (2191,20261119). Strengthening invalid-input boundaries
increased the affected suite to 54 passes, zero failures/skips. These overlapping
runs are not summed. The authored coil executes OP, AC and transient against
default ngspice at the existing 1uV/1nA plus 1e-4 relative bounds, both on CPU LU
and actual Intel, without fallback. This is an authored fixture, not a new original
deck. The routine Intel suite passed 15 tests; the UHD630 driver31.0.101.2141 FP64
probe also passed (seed18117).

Stryker.NET5.0.0 final focused replay tested 23 candidates: 19 killed and four raw
survivors. Earlier replay:16 killed/seven survived. Added negative-factor
combinations and nonempty machine-error-code checks caught three real gaps in
the tests. The four remaining <= to < variants have the same public preparation
outcome in this source: zero nominal/scale produce zero or a nonfinite coefficient;
zero M produces a nonfinite coefficient; and OperatingPointBridge independently
rejects nonpositive final inductance with the same diagnostic/range. These are
reviewed equivalences for the current public caller, not additional kills or a
proof about future direct consumers of the internal helper.

The first mutation selection mistakenly used line numbers where Stryker requires
character offsets and tested zero candidates. It supplies no score. Corrected
offsets4277..4456 select the actual frozen formula/guard. The wider instrumentation
reported3172 CompileError entries (including16 elsewhere in this file) and3594
ignored entries; those are not passing coverage or individually established
product compile failures. Only the23 actually tested candidates support the
focused result. Native reports, exact configurations, source/test hashes and the
failed attempt remain on E:. AGENTS.md now records the character-span contract.

Coverlet10.1.0 measured54/54 executable helper lines and96/112 branches using the
20-test snapshot before the three additional negative cases. This is helper-only
coverage, not coverage of the final54-test suite or the whole frontend. The exact
production helper was unchanged during these test additions. Separate parser
fuzzing was not run for this adapter; existing generated storage metadata is
reused. The seeded FsCheck property and malformed/negative tables are recorded
separately from mutation testing.

All12 retained model-L originals were inspected through Run-RequestedCorpus:
eight preparation refusals and four resource/syntax refusals remain. The first
new gaps include independent TIME sources, tuple coefficients, unsupported model
aliases, authored dialect syntax and transient budgets. Some files are intentional
negative tests. None is credited as newly agreeing, and no input or comparison
tolerance was changed. The next slice is independent TIME-source execution.

Artifacts: E:/Artifacts/Spice-next-fifty-20261119T233347Z, including the final
Stryker reports, authored-intel/summary.json, inductor-originals/summary.json,
inductor-mutant-regression-tests.log, inductor-coverage and Intel gate/probe logs.
The native release binary/profile/features are the baseline manifest; this slice
changes preparation only. Timings for additional agreeing originals remain
pending; no performance improvement is claimed. Source/main, active frozen
campaign, raw originals and CI policy are preserved.
