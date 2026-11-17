# AC performance diagnosis and verified scope

The repeated nonlinear bias solve was a real implementation defect. One SANDLER8 AC request performed 28 Newton solves; SANDLER9 performed 19. Each now performs one. `AcSweep` owns its bias and samples for one request; the public AC return and JSON contracts stay unchanged. Derived diode, charged-junction and MOS currents consume the same verified bias. Both recorded responses are exactly equal before and after the change, including independent audit results. The public-registry regression detects the old implementation (55 real provider calls versus 11 expected).

A second bottleneck was preparation of a synthetic deck for every numeric scalar. Whole optionally signed literals now reuse the generated ANTLR BODY lexer and existing engineering-number decoder, requiring exact character coverage and EOF. Other expressions use the existing grammar. A standard System.Diagnostics.Metrics counter reports synthetic expression preparations without recording input. Review caught an extra expression-budget charge; the corrected path counts exactly the original numeric/unary nodes. The f11/f12 macro expansion boundary test verifies success below and refusal above the shared budget.

Requirements AC-BIAS-REUSE-001 and PARAM-LITERAL-FAST-001 remain Draft with bounded evidence. No global bias/result cache, numerical tolerance relaxation, new parser dependency or public schema change was introduced. Pinned RSpice retains its PreparedAc linearization bias; that source inspection guided the ownership fix.

## Reproduction and evidence

Raw artifacts remain local at E:/Artifacts/Spice-performance-20261117T052423Z. Bounded results and exact hashes are in ../evidence/ac-performance.json. Scripts in that directory record commands, watchdog settings and source provenance. Benchmark helper source is scripts/Measure-RequestedPipeline.cs; build it incrementally with dotnet build, --artifacts-path on E:, the pinned AntlrJar, GrammarOutput on E:, UseSharedCompilation=false and BuildInParallel=false. File-based app builds do not accept -m:1. Invoke the built DLL with corpus, frozen inventory, engine, fresh JSON output and 1..5 repetitions. Keep original inputs and existing comparison bounds.

Native child timing excludes our preparation; ngspice includes parsing/RAW and the pinned RSpice probe includes parsing with a cached debug core. Full managed-pipeline timing includes resource resolution, generated parsing, requested preparation and native child execution. It excludes helper compilation/startup and independent comparison. Three warmups are retained before five samples; tiered JIT and OS variation remain visible. No owned build/test overlapped final measurements. These boundaries do not establish universal RSpice release parity.

## Verification and limits

Native focused regression: 41 tests passed; additional charge regression: 34 passed (four overlap). Managed review-final run: Syntax 556 passed/1 skipped, Frontend 1143 passed/42 skipped, zero failures. The skip set includes unavailable opt-in native oracle fixtures and exFAT link capability; independently requalified original decks are separate evidence. Proptest 32 cases uses seed 88320261117; focused coverage invocation sets environment seed 88320261103. FsCheck literal/full-grammar equivalence runs 128 cases with replay 2164,20261117. Malformed suffix/prefix, nonfinite input, environment tampering, invalid AC grid and separate-request bias tests ran. Stable Rust line instrumentation cannot measure branch coverage. Managed coverage is the combined tested assembly scope, not universal code qualification.

TDD failure evidence preserves both old repeated-bias work and six literal tests failing before optimization. A maintained cargo-mutants/Stryker campaign was not rerun for this checkpoint; these red/green checks are not a mutation score. General parser fuzzing was not rerun. Intel circuit gates and the matrix probe ran, but neither original SANDLER deck is newly Intel-qualified by those checks. Windows-only evidence; Linux, general SPICE, production and GPU acceleration are not qualified.

One orchestration run completed its managed tests but refused to overwrite the frozen runtime directory. The reviewed assemblies were then copied into a fresh runtime-reviewed directory; final qualification uses that copy. Preliminary timing files overlapped builds or had fewer warmups and are not the final results.

The fifty-deck objective remains 2/50. This performance checkpoint adds zero new agreeing decks. Remaining priorities are full preparation overhead, LU AC residual failures, uncovered original-deck families and transient TSTART coverage.

## Final serialized five-sample medians

| Original | Ours prepared native | RSpice probe | ngspice | Ours full warm pipeline |
| --- | ---: | ---: | ---: | ---: |
| SANDLER8 | 65.97 ms | 108.95 ms | 135.31 ms | 207.32 ms |
| SANDLER9 | 76.03 ms | 120.66 ms | 148.04 ms | 158.53 ms |

Using the same updated frontend with the predecessor core gave full-pipeline medians 452.68/195.52 ms. The paired core change reduces these to 207.32/158.53 ms (54.2%/18.9%). This comparison isolates the core change; it does not measure the separate literal optimization against an old frontend. Full pipeline is still approximately 1.90x/1.31x the measured RSpice child time, with different startup boundaries and visible JIT variation. The prepared native child is approximately 39%/37% shorter, but excludes preparation and is not an overall parity claim.
