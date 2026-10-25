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
the complex `v(2)` observable for `res_partition`; both preserve fixture hash,
declared observable and numerical tolerance in machine-readable output.
