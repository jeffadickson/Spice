# Qualification testing libraries

Reviewed primary sources on 2026-10-26:

- [Proptest 1.9.0](https://docs.rs/proptest/1.9.0/proptest/) and its
  [failure persistence](https://proptest-rs.github.io/proptest/proptest/failure-persistence.html)
  provide generated Rust strategies, shrinking and replay. Pinned dev-only,
  std-only features avoid an unused fork/timeout runtime; the outer campaign
  supervisor continues to own process deadlines.
- [FsCheck](https://fscheck.github.io/FsCheck/QuickStart.html) and
  [FsCheck.Xunit 3.5.0](https://www.nuget.org/packages/FsCheck.Xunit/3.5.0)
  fit the existing xUnit managed harness and provide shrinking/seed replay.
  Integration is planned; a researched dependency is not yet execution evidence.
- [Coverlet VSTest integration](https://github.com/coverlet-coverage/coverlet/blob/master/Documentation/VSTestIntegration.md)
  fits the current xUnit v2/VSTest harness. Keep its measurements separate from
  prior dotnet-coverage scopes; a move to Microsoft.Testing.Platform requires
  an explicit compatibility decision. Coverlet/ReportGenerator adoption is pending.

Cargo-mutants and Stryker.NET remain the mutation engines. The custom selected
numerical mutations are supplemental. Existing hand-written deterministic
numerical oracles remain useful; they are not a substitute for generated shrinking
campaigns or independent reference tools. No snapshot-library dependency is needed
for numerical acceptance because checked equations/tolerances are stronger than
automatically accepting serialized output snapshots.

Compatibility decision: registry metadata reports Proptest 1.12.0 requires Rust 1.88, above this project's declared 1.87 minimum. Pin 1.9.0 (declared minimum 1.82) instead; the active compiler is 1.99.1. No MSRV build is claimed until actually tested.
