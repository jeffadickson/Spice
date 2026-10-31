# Parser dependencies and provenance

The product syntax project references only **Antlr4.Runtime.Standard 4.13.1**.
NuGet's resolved dependency graph is pinned in `packages.lock.json`. The
generator is **ANTLR 4.13.1**, downloaded from antlr.org by
`scripts/Initialize-SyntaxParser.ps1` and SHA-256 checked. Both are BSD-3-Clause.
Primary license: <https://github.com/antlr/antlr4/blob/4.13.1/LICENSE.txt>.
Copyright (c) 2012-2022 The ANTLR Project. All rights reserved. Binary/source
redistribution must retain the upstream copyright, license conditions and
disclaimer; generated syntax code does not remove runtime redistribution duties.
Installed NuGet and generator packages contain their license notices. This slice
does not publish a binary distribution; retain those notices when packaging one.

The grammar is independently authored from language behavior/specifications.
No XDM or KiCad GPL parser code has been copied. Their dialect-specific sources
are research references, not linked or redistributed dependencies.
SpiceSharpParser is used by the separate existing assessment; the syntax product
does not reference SpiceSharp, SpiceSharpParser or a legacy simulator runtime.

Test-only dependencies follow the repository's existing versions:
Microsoft.NET.Test.Sdk 17.14.1, xUnit 2.9.3, runner 3.1.5,
FsCheck.Xunit 3.5.0 and Coverlet collector 10.1.0. Stryker.NET 5.0.0 is
the existing locally pinned mutation tool. Framework/tool availability is not
evidence that every configured check ran; see parser-only-results.md.
