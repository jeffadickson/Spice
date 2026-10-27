# Sparse transient checkpoint 404

Campaign steps 61–70 (395–404), source 20ee36c plus documentation/evidence.
Seventy of the interpreted 100 implementation steps are complete; final review
remains pending. Full build, 310 Rust tests, 29 managed checks, 61 xUnit cases,
formatting, Clippy and .NET analyzer/XMLDoc gates pass. The current Linux
managed suite also passes all 61 tests on .NET 10.0.402 against the instrumented
Linux engine. Windows SDK is 10.0.403. The legacy endurance test remains ignored.

Linear real CSC solves now use independent sparse residual traversal at the
unchanged 1e-10 componentwise threshold. BE/trapezoidal systems combine canonical
G and signed Q without dense scans, including algebraic rows and committed
history. Legacy linear BE consumes that path; configured linear dynamics can own
faer sparse factors, verify numerical identity before callback invocation and
reuse a bounded two-entry exact-step cache. Same-provider dense reference work
requires explicit Unsupported; terminal failures propagate.

Prepared callback shape/finite/residual and stale-identity tests, signed flux
equations, discrete RC recurrences, adaptive rejected-history isolation, accepted
observers and changing PWL RHS contracts pass. The new generated RC property
passed 4096 configured cases across resistance/stiffness ranges, both methods
and LU/faer; a separate 4096-case arbitrary IEEE/shape property returned finite
accepted systems or errors. Default replay seed remains 0x5350494345333535 with
Proptest shrinking/persistence; these runs reported no new counterexample.

examples/sparse-decay.json actually ran: four BE steps on an exact 0.25-second
grid, one prepared factor, three reuses, final voltage 0.8493465600000001 versus
(24/25)^4=0.8493465599999999. Debug events remain on stderr and stdout parses as
one JSON result. This is a finite discrete-equation/factor-count example,
not a speed measurement or continuous-error bound.

Actual cargo-mutants 27.1.0 dynamic selection completed in 493.369 s:
20 operator candidates in src/linear_dynamic.rs, 19 caught, one survivor,
zero timeout/unviable/unattempted. Test targets were sparse_transient and
property_campaign; the exact name filter and source hashes are retained in
sparse-404-mutation.json. The survivor changes trapezoidal previous excitation
from += to -=: existing free-decay and BE source cases did not distinguish it.
This is a concrete test gap for the next tranche, not an equivalent-mutant claim.
Prepared-provider and cache code are not within this mutation file scope.

The earlier selected AC follow-up also completed: 28 candidates, 18 caught,
three survivors, seven unviable in 369.355 s. Its three OR/AND guards remain
survivors; later finite checks may mask their effect, but no equivalence proof
or kill is claimed. The separate residual-normalization replay completed both
candidates caught in 85.676 s, no other outcomes. Their snapshots and reports
remain separate from the earlier 80-candidate partial run. Stryker was not rerun
for unchanged Client.cs; Requests.cs and helper mutation coverage remain limited.

Fresh Linux owned Rust line coverage: 4913/5443 (90.26%), with branch coverage
unavailable. Every src/test hash matches this checkpoint. All 72 LLVM mismatch
records are repetitions of two zero-hash tracing_core symbols; no owned or
unexpected symbols appear. The Linux managed run is functional evidence,
not a new managed coverage collection. Actual platform raw hashes are retained:
Linux LF bytes match Windows after explicitly declared UTF-8/line-ending
normalization. No code equivalence beyond that text comparison is inferred.
Unchanged managed client coverage remains the historical matching-source
checkpoint-394 scope: 162/170 lines, 109/112 mapped branches.

StrictDoc 0.30.4 parses 13 documents, 234 unique IDs and 220 resolving parent
links; SPARSE-009..011 remain Draft. Sphinx 9.1.0 builds with warnings as errors,
225 generated C# XMLDoc entries. New named ngspice runs were not added/rerun in
this tranche; checkpoint 394's bounded external AC results retain their provenance.

Next: close the trapezoidal previous-source gap, tighten changing-source
assembly, extend signed transient/oracle/topology cases and finish 405–414.
Then bounded models 415–434 and final deep review/fixes 435. Nonlinear transient
Newton still uses bounded reference Jacobians. Compiler dense references retain
the 256-unknown cap. The tight bridge counterexample, vendor/branch/host coverage
limitations and no-full-SPICE/GPU/production qualification remain explicit.
