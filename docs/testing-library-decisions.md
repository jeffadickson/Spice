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
  ClientProperties.cs integrates 256 replayable signal-selection/permutation
  cases per checkpoint (Replay 362,20261027), including empty generated arrays.
- [Coverlet VSTest integration](https://github.com/coverlet-coverage/coverlet/blob/master/Documentation/VSTestIntegration.md)
  fits the current xUnit v2/VSTest harness. Keep its measurements separate from
  prior dotnet-coverage scopes; a move to Microsoft.Testing.Platform requires
  an explicit compatibility decision. ManagedCoverage.cs collects with pinned
  coverlet.collector 10.1.0 and ReportGenerator 5.5.11, retaining raw reports locally
  and a bounded source-hashed summary under evidence/. Prior scopes remain historical.

Cargo-mutants and Stryker.NET remain the mutation engines. The custom selected
numerical mutations are supplemental. Existing hand-written deterministic
numerical oracles remain useful; they are not a substitute for generated shrinking
campaigns or independent reference tools. No snapshot-library dependency is needed
for numerical acceptance because checked equations/tolerances are stronger than
automatically accepting serialized output snapshots.

Compatibility decision: registry metadata reports Proptest 1.12.0 requires Rust 1.88, above this project's declared 1.87 minimum. Pin 1.9.0 (declared minimum 1.82) instead; the active compiler is 1.99.1. No MSRV build is claimed until actually tested.

QR refinement reference: https://netlib.org/lapack/explore-html/d5/da4/group__gerfs.html describes factor-reusing iterative refinement. This adapter retains nalgebra QR factors and uses at most three same-precision correction solves; it does not implement LAPACK error estimation or mixed-precision guarantees. The existing independent componentwise backward-error threshold remains unchanged.

Engineering documentation uses Sphinx 9.1.0 in a separate environment, with
transitive versions retained in docs/engineering/requirements.lock.txt. Registry
metadata reports Python >=3.12, matching Ubuntu 24.04's supported setup. Latest
9.2.0 requires >=3.13 and was not selected for the cross-platform pin. Primary
sources: https://www.sphinx-doc.org/en/master/usage/installation.html and
https://pypi.org/project/Sphinx/9.1.0/. This adds a derived engineering/API projection;
existing Markdown and canonical StrictDoc sources remain authoritative in place.

Mutation inventory checks use the existing .NET nonbacktracking regex engine,
with a bounded pattern and timeout, rather than adding a parser dependency.
Supported filter syntax is the intersection with the pinned native tool's Rust
regex syntax. Reference: https://learn.microsoft.com/en-us/dotnet/standard/base-types/regular-expression-options#nonbacktracking-mode.
Owned-package cache refresh follows Cargo's package-scoped clean command; vendor
dependencies remain cached. This is isolation/correctness work, not a measured
performance claim.

