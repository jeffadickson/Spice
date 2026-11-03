# Analog XSPICE gain subset: measured scope

The new helper prepares scalar voltage `gain` code models through the existing
generated ANTLR contexts and parameter evaluator. It adds no model kernel,
dependency, lexer or parser. The parent worktree owns selected-analysis bridge
integration; helper success alone is not complete circuit simulation support.

## Reuse decision and primary sources

The user authorized a bounded native subset rather than a production migration.
The existing typed VCVS and constant voltage source exactly express this model,
so a new behavioral runtime is unnecessary for gain. External ngspice remains
an independent executable oracle. Existing SpiceSharp/SpiceSharpParser evaluation
and archived three-way reference evidence remain the broader reuse assessment;
this change does not replace that decision or claim their full model support.

Primary source inspection used ngspice checkout
`e8285746c73a8229cf922487c8ccfcf6bea61bce`:

- [gain interface](https://github.com/ngspice/ngspice/blob/e8285746c73a8229cf922487c8ccfcf6bea61bce/src/xspice/icm/analog/gain/ifspec.ifs)
  specifies default scalar voltage input/output and offsets 0, gain 1.
- [gain equations](https://github.com/ngspice/ngspice/blob/e8285746c73a8229cf922487c8ccfcf6bea61bce/src/xspice/icm/analog/gain/cfunc.mod)
  specify output offset plus gain times input plus input offset, and real AC gain.
- [summer interface](https://github.com/ngspice/ngspice/blob/e8285746c73a8229cf922487c8ccfcf6bea61bce/src/xspice/icm/analog/summer/ifspec.ifs)
  requires vector input and vector gain/offset parameter semantics. Those are
  explicit unsupported work, not aliases for this scalar gain helper.

This is an independent implementation of the documented relation; no upstream
source or assets are redistributed.

## Verified and pending behavior

`catalog.XspiceInstances` retains lexical owner, model reference and ordered
ports. `XspiceGainLowering.Prepare(catalog, instance, environment)` returns an
atomic `PreparedXspiceGain` or source-located diagnostics. The offset is
`gain*in_offset+out_offset`. Model parameters use the declaration's lexical
snapshot, including retained enclosing-instance semantics in the evaluator.

The helper admits default voltage ports, `%v`, and `%vd` with preserved order.
It refuses unknown model families, vectors, inverted/current/digital/UDN ports,
unknown/duplicate/opaque parameters and unsupported or nonfinite expressions.
There is no summer, generic B-source, nonlinear/discrete code-model support.
The common B-source header is a composite generated lexer token, so this work
does not reconstruct its nodes with an ad hoc scanner.

Pure helper tests: 26 passed, zero skipped. The full frontend suite has 159
passed, 10 explicit native-environment skips, 169 total. FsCheck is executed by
an ordinary Fact through `Check.One` with 128 cases and seed 20261102 using
`Rnd(seed)`'s valid golden gamma. An earlier even-gamma PropertyAttribute caused
partial VSTest execution; its 19-case counts, coverage and first mutation result
are superseded and must not be reported as full verification.

Coverlet 10.1.0 measured 44/44 lines and 57/60 branches over the new helper,
extraction and port predicate: helper 26/26 lines and 31/32 branches; extraction
16/16 and 24/26; port predicate 2/2 and 2/2. Shared declaration glue, generated
ANTLR and existing runtime are outside this focused coverage scope.

Original A-card comparisons cover positive gain/nonzero offsets, negative gain
and explicit differential ports, and mixed-case model/parameter names with
engineering suffixes. The initial zero-reference variants passed 78 voltage
checks over DC, one AC frequency and a linear ramp transient on LU/faer.
Nonzero differential references pass DC/AC but the native typed transient
request returns `numerical_failure: sparse real residual failed`. The opt-in
regression remains failing until the parent runtime investigation resolves it;
no complete differential transient qualification is claimed.

Raw artifacts are under `E:/Artifacts/spice/behavior-xspice-nov2`. In particular,
`gain-oracle-ff7e0b905b2243298f069eb76869a9c0` contains the exact original deck,
ngspice data, typed request and failure response. Expected differential transient
output is `2.3 - 2000*t` volts, with t in seconds. This finite named comparison is
not a global numerical guarantee. No new standalone fuzz campaign ran; fixed
negative cases and the bounded FsCheck algebra campaign are the new input checks.

## Reproduction

Use an existing checkout, pinned ANTLR jar and .NET 10.0.403. Check the checkout,
artifact root and all parent directories before running commands. Set
`SPICE_CORPUS_ENGINE`, `SPICE_CORPUS_NGSPICE` and an existing
`SPICE_CORPUS_TEST_ROOT` only for the opt-in native regression. The native oracle
used ngspice-47+ and the existing verified-wave-engine executable; their hashes
are recorded in the evidence file.

The tested pure command is `dotnet test dotnet/Simulator.Frontend.Tests/Simulator.Frontend.Tests.csproj --no-build --artifacts-path E:/Artifacts/spice/behavior-xspice-nov2/revised-build --filter FullyQualifiedName~XspiceGainTests`.
Build first with the pinned `AntlrJar`, a unique `GrammarOutput`, and the same
artifacts path. Do not run coverage collection concurrently with a build using
the same output: its instrumentation/restore mutates assemblies.

`scripts/Measure-XspiceGainProfiles.cs` accepts repository, ngspice corpus,
frozen inventory JSON and a fresh output JSON. It checks frozen root hashes,
uses the existing resource resolver, then reports lexical A-card model families
and gain preparation profiles. It includes inactive definitions and evaluates
lexical defaults, not active hierarchy overrides or complete circuits. Counts
cannot be added to solved-circuit totals.

The corrected Stryker.NET 5.0.0 campaign killed both selected offset-arithmetic mutants. Of 429 generated mutants, 376 were outside the selected span and 51 had compile errors outside that span; this is a two-mutant check, not whole-helper mutation qualification.

Frozen928-root lexical census completed:890 resource-expanded accepted roots,38 resource refusals,zero changed root hashes. It observed 1157 A-card instances over 76 lexical families, including63 gain instances in49 roots and12 summer instances. Of gain instances,34 in31 roots prepared under lexical defaults;18 refused port semantics,4 unsupported parameters,5 invalid environments and2 missing parameters. Counts include unused hierarchy and repeated source inclusion; these are neither distinct complete circuits nor simulation uplift. The census preceded the diagnostic-ordering fix; invalid-environment classification is retained as measured. StrictDoc validation passed22documents/344uniqueIDs/333resolvedparents.
