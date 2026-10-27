# Final campaign review and fixes — iteration 435

The documented 100+1 interpretation is complete: implementation/qualification
steps 335–434, checkpoint pushes every ten, and final review/fixes 435. Review
source commits are 4e4224c, e4e906a and 3d7360d; the final evidence commit records
their results. This is a source/evidence review by the coding agent, not external
expert certification. Full M1 work packages and derived requirements remain open
or Draft where their wider acceptance evidence is unavailable.

## Findings and dispositions

| Finding | Disposition and evidence |
|---|---|
| Prepared sparse factors depended on DC analysis for residual acceptance | Move the common verifier to solver_contract; retain the public DC wrapper. Existing provider, overflow/scale, prepared identity and dynamic tests pass. |
| QR refinement could replace a better vector with a worse correction | Retain nonworsening componentwise backward error within three corrections; equal-error corrections may continue. Generated independent error checks pass. Numerical acceptance stays separate from factor success. |
| A generated test assumed raw QR always met the acceptance threshold | Reject that test hypothesis; preserve exact input in final-435-qr-counterexample.json. A unique scaled system can expose rounding on zero-RHS rows. The corrected property measures nonworsening error without inventing acceptance. |
| Legacy unitless absolute_tolerance was validated but unused | Document compatibility semantics in Rust, C# XMLDoc and Sphinx. Actual convergence uses explicit voltage/current tolerances. No silent change to historical requests or mixed-unit tolerance policy. |
| C# Ac's optional second argument did not preserve old method signatures | Iteration 431 restores one-argument constructor and deconstruction, with reflection, wire round-trip and real-engine tests. |
| Explicit nonlinear initialization omitted diode conduction from algebraic KCL | Iteration 425 includes signed conduction before observers. Three selected initialization mutations are caught; dynamic displacement freedom remains explicit. |
| Standalone managed mutations read a mutable native dependency and fixtures | Iteration 432 freezes and verifies input hashes; actual completed Stryker runs use snapshots. Tamper/source-independence probes pass. |
| Oracle helpers read raw inputs before bounding them | Iteration 430 bounds snapshots before copy/parsing; positive/oversized probes and intentional failed comparisons run. Race/timeout paths remain unexercised. |
| Standalone StrictDoc parsing did not reject global duplicate IDs | Correct a new draft collision before checkpoint 434; add a reusable real-parser plus global-ID/parent gate and use it from remote qualification. Negative identity probes pass. |
| Mutation replay exposed weak output-contract cases | Add one-byte-over-limit, nonempty diagnostics, exact missing-descriptor error, mixed valid/null arrays and failed-envelope-with-data cases. Final actual Stryker results are below. |
| Generated metadata existed but the typed client failed NativeAOT publish | Initial IL2026/IL3050 errors expose options-only serializer overloads. Use explicit generated type metadata, enable trimming/AOT analyzers, and actually publish/run the native typed charge example with independent branch assertions. |
| Handoff/status prose still described earlier checkpoints as current | Reconcile implementation status and prompt with the completed campaign, preserving historical reports and concrete remaining gaps. |

## Final execution evidence

`scripts/Checkpoint.cs -- 435` passes build, format, Clippy with warnings as errors,
345 Rust tests, 29 managed conformance checks and 70 Windows xUnit cases. Linux
passes the same 70 xUnit cases on SDK 10.0.402. Owned Rust source/test hashes match
the Windows checkout exactly; managed raw hashes are retained and separately
normalized for UTF-8/CRLF comparison, with all normalized sources matching.

`PROPTEST_CASES=4096 cargo test --test property_campaign` passes all 16 properties:
65,536 requested library cases, fixed default seed 0x5350494345333535, with replay
and shrink work excluded from that count. Existing minimized regressions remain
retained. This is generated property testing, not coverage-guided fuzzing.

Final Stryker.NET 5.0.0 run 20261027T014937Z-5e99f0e0 completes in 289.631 seconds:
**200 candidates, 88 caught, one survived, one timeout, 103 compile errors and seven
ignored**, no unattempted. Client/test hashes match final source. The immutable native dependency has its
own recorded hash, distinct from the later ordinary rebuild; see the binary
comparison report. Native build identities are not equated. Intermediate runs remain retained separately; do not compare
their changing categories as deterministic counts. The remaining survivor changes
UTF8Encoding's BOM flag on decoding-only use. Microsoft's constructor contract
ties that flag to [GetPreamble](https://learn.microsoft.com/dotnet/api/system.text.utf8encoding.-ctor);
likely equivalence is an inference, not formal proof or a caught mutant. The
stream-loop mutation times out and remains a timeout, not a test-detected kill.
DTO/helper/native mutation coverage is outside this client selection.

Earlier native selections remain separately scoped and source-hashed: charge
ten caught/four unviable of fourteen; initialization three caught of three;
dynamic operator replay twenty caught of twenty. Historical AC/Newton survivors
and unattempted/invalid categories are preserved in their checkpoint reports.
New QR refinement has generated/regression evidence, not a new native mutation
qualification claim.

Fresh owned Rust coverage is **5342/5783 lines (92.37%)**; branches are unavailable.
All 77 LLVM mapping warnings match the five previously attributed dependency
symbols, with no owned or unclassified symbols. Final Coverlet measures
**166/174 client lines and 109/112 branches**. The requirements helper separately
measures 57/60 lines and 45/62 mapped branches. Oracle helper checkpoint metrics
remain separate in models-434-oracle-tooling-coverage.json. PowerShell branches,
vendor internals, arbitrary error/timeout paths and GPU are not measured. Client
coverage was recollected after ordinary documentation builds to isolate assembly
instrumentation; percentages alone do not prove correct behavior.

StrictDoc 0.30.4 parses 14 documents; 256 IDs are unique and 243 parent relations
resolve. Sphinx 9.1 builds with warnings as errors and 230 XMLDoc entries. The
new validator also executes positive/duplicate/unresolved-parent probes. The
remote publisher's nine synthetic Pester tests pass using local bare remotes;
this does not claim a fresh full remote endurance/coverage workflow or GitHub CI.

The Windows x64 TypedCharge NativeAOT example actually publishes and executes
against the native engine, asserting g(V)+j*omega*C source current and the
conduction descriptor. Binary/source/log hashes are in final-435-nativeaot.json.
Explicit metadata follows the existing BCL
[source-generation path](https://learn.microsoft.com/dotnet/standard/serialization/system-text-json/source-generation),
without adding a serializer dependency. Whole-SDK/Linux AOT remains unqualified;
other development helpers ran managed and StrictDoc/Sphinx require Python.

Named external diode AC/transient comparisons retain their declared nominal
temperature, source phases, shared times, print/interpolation and BE truncation
bounds. The AC/DC importer bias regression is fixed; unsupported depletion,
transport, series resistance, general temperature-scaled IS and UIC import remain
explicit. Prior named linear oracles and selected upstream attribution remain.

## Remaining limits and handoff

No prepared complex reuse, general sparse rank test, automatic AC/configured
physical audit, broad semiconductor library, full SPICE syntax, GPU implementation,
global convergence/error bound, large-circuit speedup or production qualification
is claimed. Dense references and nonlinear Newton remain bounded. Manually
constructed response envelopes and thin convenience accessors are not independent
physical correctness proofs. The tight bridge counterexample remains retained.

No new full-hour/five-hour campaign is claimed. The historical one-hour run and
its two failed partial predecessors retain their own provenance. Nightly schedule
and bounded remote result-branch policy remain unchanged; raw artifacts stay local.
No CI was enabled or dispatched. Future work should begin from the reconciled
prompt and these explicit gaps rather than restarting the completed iteration quota.
