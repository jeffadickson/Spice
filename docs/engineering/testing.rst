Qualification and replay
========================

Proptest 1.9.0 provides bounded Rust generation, shrinking and persisted regression
seeds. PROPTEST_CASES controls requested cases per property and PROPTEST_RNG_SEED
selects a replay seed. The ordinary suite uses a fixed default seed. The overnight
supervisor discovers the property inventory and counts requested successful cases
only after an entire batch passes; persisted replays and shrinking are excluded.

FsCheck.Xunit 3.5.0 generates managed signal-selection/permutation cases with
Replay 362,20261027. The synthetic subprocess fixture challenges envelopes,
exit status, cancellation and UTF-8 byte limits. It is distinct from the real
engine conformance test and is not numerical evidence.

Cargo-mutants 27.1.0 and Stryker.NET 5.0.0 are the primary mutation engines.
Survivors, timeouts, uncovered and compile-error mutants remain separate from
test-detected kills. Supplemental hand-selected mutations require explicit opt-in.
Short deadlines may produce partial or unavailable outcomes.

Coverlet 10.1.0 and ReportGenerator 5.5.11 measure the owned managed client.
Rust LLVM line coverage excludes test/vendor internals; branch metrics remain
unavailable in the qualified instrumentation. Always retain source/tool identity,
exact coverage scope, warnings, seeds and unresolved counterexamples.
