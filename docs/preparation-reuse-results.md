# Preparation recognition reuse: bounded result

A resource-free root was recognized during resource resolution, then recognized again by the normal requested-deck path. ResourceResolution.Parse now retains the resolver-owned immutable recognition and reuses it only when text, character/token limits, prediction strategy and metadata flags match. This removes one generated full-deck recognition per preparation. All include/library expansion, changed records, stricter limits, feature collection and nonblank tail truncation retain fresh-recognition behavior. Root ASCII blank padding is deliberately retained only for resource-free roots; the immutable root scope and source segments therefore match the exact original text. Source byte hashes are unchanged.

The existing ANTLR runtime and immutable syntax model satisfy this integration need. No replacement grammar, lexer, cache library, global filesystem cache or simulation cache is added. Normal requested-deck, frontend corpus, syntax corpus, requested qualification and benchmark consumers use the same API. The qualification coordinator deliberately still recognizes original input independently for its separate syntax-stage evidence.

## Measured result

Serial Windows run, unchanged frozen release native engine, same current managed build in fresh/reuse modes, 15 retained warmups and five measured samples per original:

| Original | Force fresh recognition | Reuse recognition | Median change | Duplicate parse phase |
| --- | ---: | ---: | ---: | ---: |
| SANDLER8 | 113.42 ms | 108.70 ms | 4.2% shorter | 10.15 ms to 0.0007 ms |
| SANDLER9 | 104.82 ms | 89.96 ms | 14.2% shorter | 8.75 ms to 0.0008 ms |

Total full-deck attempts including semantic synthetic decks drop 14 to13 and10 to9. The separately measured child-process medians were ours57.59/59.64ms, RSpice cached debug probe88.69/84.26ms and ngspice110.13/115.12ms. Our full warm path remains about23%/7% longer than the RSpice child measurement, with different startup boundaries. Native timing excludes our preparation. Full managed timing includes resource resolution, parsing, preparation and native child execution/reporting; it excludes helper compilation/startup and independent oracle comparison. No owned builds/tests overlapped the paired measurements. Ranges overlap: SANDLER8 fresh108.19–125.95/reuse96.80–115.56ms, SANDLER9 fresh94.94–123.08/reuse87.27–95.16ms. Five samples on two inputs do not establish a general speedup or release parity.

Earlier three-warmup runs were strongly affected by tiered JIT. Their results remain raw evidence but are not compared with these fifteen-warmup medians to inflate the improvement. Benchmark helper supports optional bounded warmup count0..20 and recognition mode fresh/reuse, defaulting to the existing three warmups and normal reuse path. Source and exact hashes/versions are recorded in ../evidence/preparation-reuse.json and the local run files. Raw outputs: E:/Artifacts/Spice-preparation-20261117T060225Z.

Both original SANDLER AC decks still agree with ngspice on Faer across their requested analyses. This is zero additional decks: the fifty-deck goal remains2/50. Native engine hash remains14ebad6399cfd5f2e6f7c676f5353fe50ae9b3b016bc493ddda3309b385d2482. Rust numerical core and providers did not change; Intel/Linux gates were not rerun or newly qualified.

## Verification and limitations

Corrected final run: Syntax563 passed/1 skipped, Frontend1143 passed/42 skipped, no failures; exact TRX total1749, passed1706. New tests cover deterministic recognition count, immutable reuse, independent statements/scopes, modified record text, token/character limits, SLL/LL selection, feature/declaration flags, later changed file contents, include expansion, physical CR/LF/CRLF ranges and the include-expanded padding budget. FsCheck32 cases replay2164,20261119 compare independently parsed values, ranges and padding. Existing randomized/malformed suites also ran. Real StrictDoc parsed95 documents/581UIDs; PREP-RECOGNITION-REUSE-001 remains Draft.

Measured combined managed assembly coverage:25219/29429 lines(85.69%),12278/15534 branches(79.04%). Syntax assembly alone88.20%line/82.01%branch. These are scoped metrics from corrected-coverage, not whole-simulator coverage. Skips retain explicit native-oracle prerequisites and exFAT symbolic-link limits. No maintained mutation framework or long general fuzz campaign reran; the pre-fix TDD recognition assertion failed with3 recognitions instead of1. That is not a mutation score. Initial array assertions compared ImmutableArray identity and were corrected to compare elements.

An initial even FsCheck gamma20261118 was invalid. FsCheck threw asynchronously; VSTest returned zero and misleading partial pass totals. Those runs are unavailable full-suite evidence. Correcting gamma to20261119 and checking both exact TRX inventory and absence of unhandled exceptions produced the complete1706-pass result. A serialized runner configuration alone did not correct that problem. Keep raw partial results; do not cite them as full passes.

Fresh review found that retaining padding for include-expanded roots could cross an established expanded-input limit. The fix retains blank padding only when the root has no resources; an explicit boundary regression passed. The reviewer reinspected without further actionable findings. All workloads used the existing Job Object watchdog with1536MiB per-process and3072MiB aggregate limits, one tree at a time; no free-RAM admission gate.

The next measured preparation target is the remaining semantic synthetic-deck recognition, not looser validation or numerical tolerances. First-use .NET/JIT startup is a separate unresolved cost.

Normal consumer builds (Syntax.Corpus, Run-RequestedDeck.cs and Run-FrontendCorpus.cs) succeeded. The compiled Run-RequestedDeck executed the unmodified SANDLER9 original and wrote its complete nine-file archive (1,417,595 bytes). Its recorded cold job elapsed1.401seconds; qualification is explicitly not_performed by that CLI, with independent agreement established separately above. That cold archival workflow is much slower than the warm helper path, so the optimization does not establish cold standalone parity. The first helper invocation itself took767ms (resolve309ms, prepare224ms, execute/report234ms), versus its subsequent warmed samples. Keep first-use preparation and archival costs visible in future profiling rather than citing warm-only numbers as the user experience.
