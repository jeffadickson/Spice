# Requested integration options and control constants

The original-corpus replay selected 44 paths with .CSPARAM or explicit
MAXORD from the frozen directive audit. All 44 input identities and expanded
deck hashes matched the earlier 928-path census. Each reached an explicit
preparation refusal. The original execution count was zero before and
after, and the native agreement count was zero before and after.

Secondary diagnostics included 12 unsafe control tokens, eight unsupported
OUTFILE settings, seven unsupported active cards, and four attempts to write
an exported constant. Diagnostics can overlap. The two MAXORD examples both
ask for third-order Gear and remain explicitly outside this integration
profile. Other numerical options, device Gmin, convergence tolerances,
control loops, netlist conditionals, S-parameters, and audio remain open.

Four independent fixtures all executed and agreed with ngspice-47+ on their
compared circuit observables. Gear order one, trapezoidal order one, and
trapezoidal order two were exercised with RC UIC decks. Each comparison
records the excluded zero-time sample. The original .CSPARAM fixture
exported a deck parameter expression and both reporting paths emitted
gain=2. These fixtures are separate from the original-deck count.

Windows replay tested base 235e44e plus the patch with the existing engine
and corpus e8285746c73a8229cf922487c8ccfcf6bea61bce. Both runs completed
with valid provenance. Original replay elapsed 33.08 seconds. Fixture
replay elapsed 5.68 seconds. Artifacts are under
E:/Artifacts/Spice-numerics-csparam-235e44e. Use the documented
Run-RequestedCorpus.cs invocation with inventory.json for originals and
fixture-inventory.json with the fixtures directory for independent probes.

SHA-256 values:

- Original inventory 43fdf83a6b273566792f68a8a9e47ff768fbfc486aaef16c39fd1ebf7d78f213
- Original summary 93c93caf3ef423b7a0e2e60647e85c99c1cb178bf37f53e63e548ff545ce2e3b
- Fixture summary 3601e255fe8439e7d276cf01f825910118630af2452571be5e6f7b11db3fdcf8

Frontend tests passed 590 with 37 opt-in skips. An earlier focused run
with the engine prerequisites enabled passed 57 without skips. Syntax
passed 471 with one filesystem symlink skip. StrictDoc parsed 52 documents,
424 unique IDs and 392 resolved parents. The FsCheck property requested
100 cases with seed 883,20261107. Negative tests exercised malformed,
duplicate and reserved exports and unsupported integration orders.
Both deliberate changes were detected in one disposable mutation snapshot.
Changing the first-order method yielded two failures, and dropping the
reserved-name guard yielded one. The other nine focused tests passed.

Coverlet measured 77.14% line and 59.37% branch coverage across managed
assemblies in the broad frontend run. RequestedControlConstants.cs measured
91.66% line and 82.35% branch. DeclarationExtraction.cs measured 97.68%
line and 81.49% branch. Standalone fuzzing and Rust coverage were outside
this checkpoint.
