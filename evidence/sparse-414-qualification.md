# Sparse transient checkpoint 414

Campaign steps 71–80 (405–414), source 9b5dd34 plus documentation/evidence.
Eighty of the interpreted 100 implementation steps are complete; the final deep
review remains pending. Full build, 324 Rust tests, 29 managed checks, 61 Windows
xUnit cases, formatting, Clippy and analyzer/XMLDoc gates pass. Linux also passes
61 managed cases on .NET 10.0.402; Windows SDK is 10.0.403. One legacy endurance
test remains intentionally ignored.

Selected time-domain source amplitudes now avoid nominal subtraction. A
1e308-to-1 voltage/current override first reproduced an incorrect zero. A second
regression exposed overflow from grouping unbound currents ahead of an unchanged
-1e308 override; compiled current descriptors now preserve original source order.
Both failures remain reproducible in tests and local red logs. A new Proptest
KCL/selection property passed 4096 configured cases across current magnitudes,
polarities and selected identities, with shrinking/persistence and default replay
seed 0x5350494345333535. No generated counterexample was reported by that run.
Source time is validated even when no waveform override is present.

Configured equilibrium initialization and explicit algebraic checks traverse
CSC. Exact-step factor reuse/eviction now has direct lifecycle tracing and
bounded-cache/adjacent-IEEE-key tests. Wrong factor identity, bad callbacks,
terminal failures and rejected trial isolation remain covered. The original
trapezoidal previous-excitation survivor is now distinguished by nonzero-source
closed-form charging. The actual repeated dynamic operator selection completes
20/20 caught in 402.139 seconds, no survivors/timeouts/unviable/unattempted.
Its scope is src/linear_dynamic.rs operators with lib/sparse_transient/property
targets, not the full numerical core. Source/provider/cache code outside that
selection has no new mutation score. Stryker was not rerun for unchanged Client.cs.

A legal active VCCS case exposed successful faer factor construction on a
numerically empty system before an invalid solve. The adapter now rejects exact
empty numeric rows/columns before real/complex preparation and solving, including
legacy dense entry points. Independent row-only/column-only and pure-imaginary
support tests pass. This necessary singularity check is not a complete rank or
near-singular conditioning test. A solvable active recurrence remains accepted;
no passivity or continuous-stability proof is inferred.

Actual ngspice 47+ named transient comparisons pass through LU and direct faer:

| Original case | Named voltage/shared seconds | Absolute bound |
| --- | --- | --- |
| RC finite ramp | v(out), 0/0.125/0.25/0.5/1 | 2e-6 |
| Signed RLC natural decay | v(out), 0.25/1/2/4 | 2e-6 |

Both grids use explicitly reported piecewise-linear interpolation without
extrapolation. Input/executable/helper hashes are retained. Six parser/interpolation
contracts and an intentional zero-source/reference mismatch verify failed report
publication and nonzero exit, followed by positive reruns. RC import selects the
documented BE policy with its own analytical finite-grid bound; the external
hand-authored request explicitly selects trapezoidal. RLC IC/UIC importer support
is not claimed. The RLC Rust test independently checks damped response, branch
KCL and the trapezoidal discrete-energy identity. These original decks are not
attributed upstream corpus growth: the selected upstream default-PULSE RC fixture
remains blocked rather than silently changing its cycle/event semantics.

Fresh Linux owned Rust line coverage is 5044/5525 (91.29%); branch coverage is
unavailable. All recorded src/test hashes match. LLVM emitted 75 warnings:
72 tracing_core records plus three additional zero-hash tracing span records.
The latter were outside the previous allowlist and were explicitly attributed
to tracing 0.1.44 do_enter/do_exit/Entered::drop source definitions; the report
preserves that distinction. No owned or unclassified symbol remains. Dependency
internals, GPU and broad host/path coverage remain unmeasured.

The new transient oracle helper measures 121/124 source lines (97.58%) and
118/156 mapped branches (75.64%) using direct dotnet-coverage Cobertura.
Timeout/pipe-limit paths and arbitrary ngspice formats remain unqualified.
Managed client coverage remains historical matching-source checkpoint-394
evidence (162/170 lines, 109/112 branches); the Linux managed run is functional
evidence, not a new coverage collection. Platform raw hashes and declared
UTF-8/CRLF-to-LF content comparisons are retained.

StrictDoc 0.30.4 parses all 13 documents: 237 unique IDs and 223 resolving parent
links. SPARSE-012..014 remain Draft. Sphinx 9.1.0 builds with warnings as errors,
225 generated C# XMLDoc entries.

Next: bounded model contracts/implementation 415–434, then final review/fixes 435.
Keep the carried architecture/API/tolerance/refinement/mutation-resource findings
for that review. Nonlinear transient Newton remains bounded reference work;
compiler dense references retain the 256-unknown cap. The tight bridge failure
and lack of full SPICE/GPU/production qualification remain explicit.
