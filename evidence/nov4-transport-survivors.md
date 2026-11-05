# November 4 transport survivor triage

The overnight Stryker 5.0.0 report on de5d84e had five survivors in Client.cs.

| Mutation | Disposition |
| --- | --- |
| CreateNoWindow set to false | Open. The existing probe is insensitive when the test host has no attached console. |
| Cancellation check removed from pending task loop | Open. Existing end-to-end cancellation tests do not isolate cancellation during inline task completion. |
| Process cleanup removed | Direct source replay failed both cancellation cases on the live-child assertion. The discrepancy with the Stryker classification remains open. |
| Cancellation check removed from bounded stream read | Open. Existing asynchronous pipe cancellation does not isolate a successfully completed read racing cancellation. |
| UTF-8 encoder preamble flag enabled | Equivalent at the decode-only GetString call. Added a fixture regression for a real wire BOM. |

The managed suite passed 85 tests with the retained corpus engine. Coverlet measured
90.67% line and 57.06% branch coverage for Simulator.Client. The custom artifact
layout required staging the built fixture beside the test assembly. StrictDoc
parsed 51 documents with 409 unique IDs and 381 resolved parent links. Cleanup
replay and raw coverage are under E:/Artifacts/Spice-nov4-transport.

The follow-up campaign uses a short NTFS source path with bulk outputs on E:.
This tests the long-path hypothesis for the prior baseline LNK1104.

## Follow-up on the three open transport mutations

The production launch settings and two inline cancellation checkpoints now have
deterministic tests in TransportCancellationRaceTests. Each of the three direct
source replays failed its targeted assertion, and the restored suite passed all
88 tests with zero skips using the retained corpus engine. Coverlet measured
90.67% line and 57.06% branch coverage for Simulator.Client.
Raw mutation and coverage logs are under E:/Artifacts/Spice-nov4-races.
The launch test verifies process configuration on the headless runner.
The Stryker-native classifications await a fresh framework run.

## Issue #38 native replay

The 9ecef1d native Stryker run killed the three targeted mutations. It reported
186 killed, two survived, one compile error, one uncovered and 14 ignored.
The cleanup survivor removes only the exit wait. The prior source replay
removed both the kill and the wait, which explains the differing outcomes.
The separate kill-deletion mutation was killed by Stryker.

The follow-up keeps process ownership until a termination acknowledgement.
A controlled incomplete task makes removal of the wait fail deterministically.
The restored managed suite passed 89 tests with zero skips. Coverlet measured
line rate 0.9067000000000001 and branch rate 0.5706 for Simulator.Client.

Cargo-mutants completed shard 4/1024 against library tests in the same
short-path run. All five candidates were attempted, with two survivors and
three compile-invalid candidates. The CLI survivor requires binary tests,
and the transient survivor requires testing the step-budget boundary.
Evidence root: E:/Artifacts/Spice-issue38-replay.

Final Stryker replay on a3207a4 completed with 188 killed, one survived
(the decode-only preamble flag), one compile-invalid, one uncovered, and
14 ignored. There were no timeouts or unattempted candidates. The exit-wait
deletion is detected. Summary: 20261105T142723Z-a4614eb4/summary.json.
The uncovered branch is the diagnostic for Process.Start returning null.
The compile-invalid result came from Count-pattern instrumentation.

The exact-name Rust replay failed closed because the native inventory also
returned two out-of-scope names. A second replay of shard 4/1024 with the
existing import_cli and transient integration suites caught both previously
surviving mutations. All five candidates were attempted, with two killed and
three compile-invalid. Summary: 20261105T142730Z-8c363770/summary.json.
