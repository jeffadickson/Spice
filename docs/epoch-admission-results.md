# First admission checkpoint for the 4,411-candidate epoch

The frozen campaign remains on clean production/test pin30a5a9a. This later
working-tree qualification neither edits that source nor rewrites its outcomes.
Read evidence/epoch-admission-results.json for the exact tested snapshots, native
reports, counts, input hashes and remaining feature/coverage limits.

The first bounded review covers nine candidates: two AC finite-operand guards,
two adaptive admission/error guards, two subsequently observed voltage/current
budget selectors, two CLI size comparisons, and one Intel-only CLI dispatch.
New tests exercise independently invalid adaptive bounds, a valid maximum-step
boundary, AC nonfinite operands with a provider-call spy and a valid analytic
2+3i control, and valid divider JSON at1MiB-1,1MiB,1MiB+1 bytes. Whitespace
padding preserves request semantics. Oversize input must be ResourceExhausted;
the exact inclusive boundary must execute successfully.

The newly observed adaptive selector needs distinct voltage/current tests. With
coarse[1,1], fine[0.5,1], voltage absolute0.2 and voltage relative0.1, the expected
first-order error is0.5/(0.2+0.1)=5/3. Restart current budgeting must not change a
voltage error. A branch-current perturbation uses its own0.01 absolute and0.2
relative budget, giving50/21. These analytical checks do not reuse controller
implementation logic as a numerical reference.

Three guard mutations preserve rejection under the documented stable InvalidInput
contract: AC source assembly independently rejects nonfinite RHS components before
provider work; adaptive error computation independently rejects nonfinite scale/
error. Circuit validation ensures an independent current source has distinct
terminals, so a bad phasor cannot disappear without reaching a RHS row. The
private adaptive tests cover independently nonfinite coarse/fine values and both
orders. Error text can differ; error.rs explicitly makes messages contextual
rather than API identifiers. These are reviewed rejection equivalents, not kills
or byte-identical diagnostic claims. Keep the useful early guards intact.

The main --intel-device mutation is compiled out of the default-feature epoch.
That is unavailable feature coverage, not equivalence. A separate maintained
cargo-mutants replay enables intel-opencl and runs real Intel backend and CLI
contracts against the trusted provider; its outcome is recorded separately.
The first feature-proof launcher passed Cargo flags as test arguments and was
stopped before qualification; its raw artifacts remain unavailable evidence.
The corrected invocation uses --cargo-arg for locked and target selection.

Normal, instrumented and mutation target directories remain separate from the
active epoch cache. Everything new is on E:. Scoped LLVM line coverage measures
ac/adaptive through the named integration test. Its CLI child is separately
verified but uninstrumented, and cfg(test) helpers are outside that library
measurement. Stable Rust branch coverage is unavailable. The full default suite,
StrictDoc, aggregation inventory, Intel routine gates and separate SpMV probe
are recorded in the evidence; no new source deck, production numerical behavior,
full SPICE compatibility or GPU acceleration is claimed.

A later relative-budget selector survivor was caught by the same SI-budget test
without another implementation change. A separate one-candidate native replay
records that result. The bounded first review now covers nine candidates:
five default-feature regressions caught, one Intel-enabled candidate caught,
and three reviewed rejection equivalents. The frozen campaign still records
its original outcomes; these follow-ups do not retest the remaining inventory.
