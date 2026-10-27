# Curated ngspice compatibility corpus

These fixtures are copied verbatim from the local ngspice checkout at revision
`e8285746c` for a narrowly declared parser/solver compatibility check. ngspice
states that its test/example files are generally under its Modified BSD license;
see the source checkout's `COPYING` for exceptions. This repository does not
claim general ngspice compatibility.

Each imported fixture has a Spice-specific test that states the supported
constructs and expected outcome. Unsupported directives are not silently
executed; the `parse_ngspice_basic` importer only ignores analysis/output
directives for fixtures whose circuit topology it can represent.

`scripts/Compare-NgspiceOracle.cs` independently compares `res_simple`'s
source current. `scripts/Compare-NgspiceAcOracle.cs` independently compares
the complex `v(2)` observable for `res_partition`, and
`scripts/Compare-NgspiceLowpassOracle.cs` compares the first complex `v(2)`
point for `lowpass`; all preserve fixture hash, declared observable and
numerical tolerance in machine-readable output. The lowpass oracle's 5e-8
bound reflects ngspice `.print` text rounding, not a general accuracy claim.

The `qualification-*` decks are original Spice qualification inputs, not copied
upstream corpus files. `qualification-series-rlc` compares the named `v(out)`
at 100/1000/10000 Hz with a 1+0.5j source against ngspice through both LU and
direct faer. Use `scripts/Compare-NamedAcOracle.cs` with its deck/request,
`v(out)` and absolute component tolerance `5e-7`; printed decimal precision
limits that external comparison. Independent higher-precision impedance checks
are in `tests/sparse_ac_circuits.rs`. The runner disables user spiceinit and
records verbatim deck, request, engine, oracle and helper hashes.

`qualification-vccs` adds an original controlled-current-source reactive-load
case at 10/100/1000 Hz with -0.75+1.25j excitation. Its named `v(out)`
comparison uses the explicit `5e-6` absolute component bound for printed values
up to three volts. Both original decks also pass bounded importer-to-analytical
equation checks; this is not upstream corpus compatibility by attribution.

`qualification-rc-ramp` is an original finite-ramp transient case. Compare
`v(out)` at 0/0.125/0.25/0.5/1 seconds using
`scripts/Compare-NamedTransientOracle.cs`, its deck/request and `2e-6`
absolute bound. Both recorded grids are interpolated explicitly; print rounding
and finite stepping limit the comparison. The importer separately selects its
documented BE policy, tested against an analytical ramp response with its own
finite-grid bound. The curated upstream `tests/general/rc.cir` remains blocked:
default PULSE durations/cycle overlap and exact event semantics are not silently
mapped onto this original PWL case.

`qualification-rlc-decay` is an original initial-condition RLC case. The
explicit typed state [v(out),v(mid),i(L1)]=[1,1,0] corresponds to the deck's UIC
request; importer support for UIC/IC syntax is not claimed. Compare named
`v(out)` at 0.25/1/2/4 seconds with `2e-6` absolute bound. The ngspice UIC
grid need not include t=0, so the runner never invents an initial reference row.
Independent damped-response, branch-KCL and trapezoidal discrete-energy checks
are in `tests/sparse_transient.rs`.

`qualification-diode-constant-charge` is an original zero-bias AC case at an
explicit 27 C, matching ngspice's nominal temperature. Its CJO mapping explicitly
sets M=0, RS=0 and TT=0. The native fixed-IS/thermal-voltage profile is not a
general temperature-dependent junction model. Compare named `v(out)` at
100/1000/10000 Hz through the named AC runner with `1e-6` absolute component
bound; printed precision and ngspice's residual default Gmin limit the finite
comparison. The imported request also passes an independent small-signal
transfer equation with the declared temperature.
