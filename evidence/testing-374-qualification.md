# Numerical/property checkpoint 374

Campaign steps 31–40 (365–374), parent b000115 plus the recorded checkpoint patch.
Forty of the interpreted 100 implementation steps are complete; the final review
has not happened. Checkpoint-374.json records 274 Rust tests, 29 real-engine managed
checks, 58 xUnit cases, build/fmt/Clippy and managed analyzer/XMLDoc gates passing.
Three current framework Pester contracts and 13 supervisor/publication/launcher
contracts passed in their separate instrumented runs.

Proptest now generates byte/Unicode parser inputs, IEEE norms, CSC boundaries and
independent sparse products, divider voltages/currents, diode derivatives and the
near-zero differential limit. A generated divider shrank to exponents
5.931013986326426 and 0 at -45.062082744055054 V: dense QR exceeded the unchanged
1e-10 componentwise backward-error threshold before bounded factor-reusing
refinement. Configured 4096-case divider testing then passed across LU/QR/faer.
The near-zero oracle exposed exp(x)-1 cancellation; exp_m1 corrects it. The diode
properties also passed configured 4096-case runs. Persisted inputs and explicit
counterexample JSON are retained. Deep-reverse finite differences require a
documented rounding-amplification allowance; that limits this oracle's precision
and does not change production acceptance thresholds.

The live Proptest supervisor smoke used a clean e839b20 snapshot, seed 369 and a
120-second maximum: elapsed 119.115 seconds, 549 completed batches, eight properties
at 128 requested cases each, 562176 requested generated cases, no pending batch or
reported failure. Replay/shrink attempts are excluded. Mutations were explicitly
skipped. This is neither the historical full hour nor a completed five-hour run.
The Git-postman schema now preserves and validates this case-count meaning while
retaining legacy imports; local bare-Git fixtures are synthetic publication tests.

Native cargo-mutants results remain separate:

| Run | Outcome |
| --- | --- |
| 354 all-test, 20-minute bound | Partial: 25 caught, one survived, seven unattempted of 33 |
| 371 combined lib/hardening | Complete: 31 caught, two survived of 33 |
| 374 after signed-zero regression | Complete: 32 caught, one survived of 33; zero timeouts/unviable |

The remaining > versus >= increment-bound survivor has an equality branch that
computes a unit limiter; source inspection suggests equivalent behavior within
validated inputs. It remains a survivor, not a kill or formal-proof claim.
The other boundary survivor was distinguished by a -0 correction and is now caught.

Stryker.NET 5.0.0 completed the updated Client.cs scope in 310.716 seconds:
200 candidates, 82 caught, seven survived, one timeout, 103 compile errors,
zero uncovered and seven ignored. Timeout/compile-error/ignored outcomes are not
kills. Large compile-error coverage and surviving cases remain limitations.
The earlier 180-candidate report is retained separately; sources/scopes differ.
Materialized-manifest tests individually challenge null fields, null array entries,
wrong vector lengths, unsupported outer versions and missing real samples.
Four pre-fix failures are retained locally in manifest-374-red.log.

A later bookkeeping check exposed a genuine cache defect: Cargo called a previous
mutant test binary Fresh for an unchanged-source baseline. The supervisor stopped
on that failed baseline. It now refreshes only owned package artifacts before every
baseline, preserves vendor caches and refuses linked target paths. Two repeated
unchanged-source two-mutant runs each passed baseline and reported one caught/one
survivor. Native field-deletion selection was also observed outside a name filter;
the wrapper now checks the actual inventory and refuses escaped selection before
mutation. A successful zero-candidate bookkeeping run is not mutation evidence.
See framework-cache-374.json, MUT-003 and the instrumented scope tests.

Coverage:

| Scope | Lines | Mapped branches |
| --- | --- | --- |
| Linux owned Rust | 4541/5026 (90.35%) | unavailable |
| Owned managed client, Coverlet | 161/169 (95.27%) | 109/112 (97.32%) |
| Framework supervisor | 110/128 | 130/198 |
| Remote publisher | 125/162 | 192/270 |
| Endurance supervisor | 147/177 | 86/136 |
| Engineering-doc helper | 43/44 | 35/52 |
| Checkpoint helper | 40/41 | 21/30 |

All instrumented Rust source/test hashes match the checkpoint. LLVM's 64 warning
records are two repeated zero-hash tracing_core dependency functions, with zero
owned/unexpected records (llvm-mapping-374.json). Client test/helper hashes and
SDK identity are retained with Coverlet. C# helper scopes include explicitly
synthetic orchestration; mapped branch maxima are not path completeness.
ManagedCoverage.cs orchestration, PowerShell subprocess coverage, full installer
and Linux helper paths, vendor internals and current Windows Rust instrumentation
remain unmeasured. Raw collectors/logs stay local.

Sphinx 9.1.0 builds engineering HTML with warnings as errors and 221 compiled-XML
member entries; canonical StrictDoc remains authoritative. All 12 SDoc files parse:
223 unique IDs, 208 resolved parent links. New requirements remain derived Drafts.
Windows NativeAOT fixture publish/echo passed; this is not SDK/simulator NativeAOT
or Linux native-fixture qualification.

The strict bridge case at 1e-16 A still fails at source scale 0.482421875 on current
Windows GNU source; its original evidence and current replay are both retained.
No ngspice oracle was rerun here. Sparse AC/transient, further model contracts,
full SPICE/GPU and production qualification remain subsequent work.

Reproduction: Checkpoint.cs -- 374; property_campaign with documented Proptest
environment controls; ManagedCoverage.cs; Setup-EngineeringDocs.ps1; framework
commands/scopes in the bounded mutation JSON reports. Report hashes correlate raw
local artifacts under build/qualification-campaign and work/framework-mutations.
