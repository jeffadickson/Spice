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
