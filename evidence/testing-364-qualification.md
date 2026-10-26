# Library and transport checkpoint 364

Campaign steps 21–30, iterations 355–364. Source parent 8dac757 plus checkpoint
documentation/evidence. Full Checkpoint.cs 364 passed build, 267 Rust tests,
fmt/Clippy warnings denied, 29 managed conformance checks and 37 xUnit cases.
The property test methods include 256 Proptest cases (default seed
0x5350494345333535) and 256 FsCheck cases (Replay 362,20261027); generated case
counts are not additional xUnit/Rust method counts.

Proptest 1.9.0 is pinned dev-only/std-only to respect the declared Rust 1.87
minimum (library minimum 1.82). The initially evaluated 1.12.0 requires 1.88 and
was rejected before integration. Active testing used rustc 1.99.1, not an MSRV
qualification build. FsCheck.Xunit 3.5.0 uses the existing xUnit v2/VSTest runner.
Both libraries own generation/shrinking; no failing counterexample was fabricated.

The controlled subprocess fixture exposed 12 malformed-envelope gaps before
runtime validation (local 357-red.log). Tests now cover supported/version/type
boundaries, completion/error consistency, native exit correlation, preserved typed
failures, pre/post-start cancellation, both pipe limits, invalid UTF-8, multibyte
byte counting and exact 1 MiB request/32 MiB diagnostics acceptance. A collection
run exposed a fixture acknowledgement race; atomic publication after closing the
writer corrected it before the full successful rerun. Fixture outcomes are
transport evidence; RealEngineConformance remains the numerical integration test.

Coverlet 10.1.0 and ReportGenerator 5.5.11 actually executed successfully:
158/166 owned mapped lines (95.18%) and 90/98 branches (91.84%). See
managed-coverlet.json for source/report hashes and raw local report location.
Generated metadata, tests, fixture, native/vendor code are excluded. These
mappings differ from historical dotnet-coverage, so no direct score comparison
is made. The coverage helper itself and full installer/platform paths remain
unmeasured; helper/fixture NativeAOT execution is not claimed.

Owned Rust source hashes still exactly match the instrumented checkpoint-354
snapshot: 4,523/5,002 lines (90.42%). This is a prior matching-source measurement,
not a newly instrumented Proptest run. llvm-mapping-354.json attributes all 62
warnings to two duplicated zero-hash tracing_core dependency functions; none
names owned simulator code. Rust branch metrics/current Windows instrumentation
remain unavailable. Raw warning evidence is retained rather than suppressed.

The native cargo-mutants all-test limited_candidate run remains active at this
checkpoint; partial observed counts are not published as final outcomes. Its
source snapshot is isolated from these changes. Stryker has not yet been rerun
against the new transport tests. The legacy overnight loop has not yet migrated
to the library runner; that remains an explicit task.

All 12 StrictDoc files parse with 0.30.4; 220 unique UIDs and 204 resolved parent
links checked. PROP-001 and MAN-001 remain derived Draft requirements. No ngspice,
full SPICE, GPU or production release qualification is inferred.

Reproduction: Checkpoint.cs -- 364, cargo test --locked --test property_campaign,
dotnet test dotnet/Simulator.Client.Tests/Simulator.Client.Tests.csproj, and
dotnet run --file scripts/ManagedCoverage.cs. Raw artifacts remain under ignored
build/qualification-campaign and build/managed-coverage.
