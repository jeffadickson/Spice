# Resource-policy results — 2026-11-01

Implementation checkpoint: `550365f`. This fixes two common resource-stage
blockers without adding another parser or changing Rust numerics. ANTLR 4.13.1,
Syntax/Frontend/Client boundaries and the prior reuse decision remain unchanged.

`ResourcePolicy.ParsingLimits` now reaches every root/fragment parse. The corpus
also uses its selected limits for expanded recognition. Interactive defaults
remain unchanged. `.csparam` exports no longer masquerade as resource-branch
selection; their parameter semantics remain deferred and real conditionals remain
refused. Parser refusals now expose at most three 512-character reasons with
physical file lines; unknown location remains line 0, never an invented line 1.
All failures still publish no partial successful text or resource graph.

## Completed corpus evidence

Same inventory and identical hashes for all 928 root inputs. The primary replay
completed in 424.552 seconds: 916 accepted structures, four encoding refusals and
eight previously adjudicated invalid/non-deck exclusions. Explicit Latin-1 replay
adds four distinct valid inputs; its 18 repeated repository decks are not counted
twice. Root syntax acceptance remains **920/920**. This does not certify vendor
libraries reached through those roots, binding, lowering or numerical support.

Resource success improves **834 → 876**, with refusals **86 → 44**. Thirty former
conditional-profile refusals and 12 former parse-budget refusals now resolve.
One former conditional and two former budget refusals now correctly expose denied
external paths. No filesystem or encoding policy was relaxed. The final replay
uses a 180-second worker limit versus the previous 90 seconds; this is not a
performance/speedup comparison. Both remain explicitly bounded campaigns.

Remaining resource outcomes:

| Outcome | Roots |
| --- | ---: |
| Vendor/resource syntax gaps | 15 |
| Denied external paths | 13 |
| Unavailable resources/I/O | 5 |
| Unsupported fragment `.end` | 4 |
| Strict UTF-8 refusal of legacy-resource bytes | 4 |
| Conditional-profile refusals | 3 |

On the **same 876 decks**, references total 61,926 before expansion and 63,683
afterward. Definitive bindings rise from 3,216 to 62,667; deferred references fall
from 58,382 to 86. Expanded outcomes also include 806 unsupported, 28 ambiguous,
66 missing and 30 invalid references. Expansion adds references, so denominators
differ. This larger cohort must not be compared directly to the previous
834-deck cohort as though the scopes were equal. None is a simulation count.

The new physical diagnostics identify a shared next grammar gap: vendor voltage
references such as `V(VC+,VC-)`, `V(REF+,REF-)` and `V(IN1+,IN1-)` are confused with
arithmetic. Inferred draft **PRS-REF-001** records OPA1611, TLV6001, TLV9002 and
TLC555 examples. It is unimplemented/unverified in this checkpoint. The next step
is generated-parser-only vendor-fragment tests plus independent dialect checks,
not speculative behavioral-model implementation.

## Verification and limits

- 354 Syntax/semantic tests and 33 Frontend tests passed. One filesystem-link test
  is skipped because E: uses exFAT. The added FsCheck property runs 64 cases with
  replay `904,20261101`; existing malformed-input/binding/parameter properties ran.
- New regressions cover explicit root/fragment token budgets, character/prefix
  accounting, control exports versus actual conditionals, bounded long-token
  messages, three-message truncation, LF/CRLF/CR, unknown budget positions, root
  positions, nested conditionals, exact source maps and immediate `.end` tails.
  Actual behavioral failures were observed before the corresponding fixes.
- The unchanged Rust executable passed four named DC fixtures on LU, QR and Faer:
  12 analytical/ngspice/independent-typed comparisons and 24 Rust solves, within
  1e-10 absolute plus 1e-10 relative tolerance. Exact executable, managed assembly
  and full relevant source hashes are retained. No new Rust build was necessary.
- Stryker.NET 5.0.0 Basic, selected changed character spans: the first campaign
  killed 10 and left seven survivors. A diagnostic-only intermediate run killed
  three and left three. Final tests killed **17**, leaving **two equivalent
  candidates**; no selected timeout/uncovered outcomes. These are selected
  profiles, not whole-assembly scores or closure of the old survivor ledger.
- Final surviving ID 639 changes OR to AND in the parser outcome/catalog guard;
  the internal parser publishes a catalog exactly for accepted input. ID 743
  changes `>512` to `>=512`; copying all 512 characters preserves string value.
  Revisit both dispositions if these invariants change. The tool also generated
  148 assembly-level compile errors and ignored 588 out-of-span mutants; those
  are unavailable/unselected evidence, not kills. Raw reports separate the files.
- **Coverage remains incomplete.** The collector's instrumented subset reports
  807/819 lines (98.5%) and 969/1110 branches (87.2%). Captured resolver/bridge
  functions and diagnostic projection are omitted from raw Cobertura. These
  percentages are not full handwritten/core coverage. Generated ANTLR is excluded;
  Rust, corpus coordinator and qualification helpers were not measured.
- Real StrictDoc 0.30.4 parses 21 documents, 321 IDs and 309 parent relations.
  SEM-RESOURCE-002/003/004 and PRS-REF-001 remain Draft. Sphinx 9.1.0 builds with
  warnings as errors and compiled API XML documentation.
- AI source review found the initially unbounded grammar-gap messages. A
  10,000-character red regression demonstrated it; the bounded fix and additional
  boundary tests passed. Follow-up inspection found no important source issues.
  This was not an external human review.

No separate coverage-guided fuzz campaign, new soak, native mutation/coverage,
CI dispatch, whole-corpus numerical run, model/AC/transient text bridge, Linux/GPU
or production qualification is claimed. File-app helpers ran managed, not AOT.

## Evidence and continuation

[Issue #16](https://github.com/jeffadickson/Spice/issues/16) has one results checklist
for this completed replay. Bounded evidence is `evidence/resource-policy-*.json`;
`evidence/resource-policy-burndown.svg` is the updated gate chart. Raw logs,
resource fingerprints, reports and reproducible source snapshots remain under
`E:\Artifacts\Spice-resource-policy-20261101T170409Z`. The preliminary corpus was
intentionally stopped before the review fix and is not qualifying completion.
The whitespace failure while adding the new requirement was corrected and
StrictDoc was rerun successfully. Source hashes, rather than the dirty base
`7f259c8` alone, identify the actual executed implementation.

Reproduction: use the checked-directory setup in `docs/semantic-frontend.md`.
Run `Run-SyntaxCorpus.ps1 -CollectSemantics -RequireValidSamples` with explicit
`-CorpusRoot`, `-ResourceRoots`, `-Workers 4 -WorkerSeconds 180 -Minutes 30`,
`-MaxInputBytes 4194304 -MaxTokens 2000000`, fresh `-Output` and E: `-ArtifactsPath`,
and the pinned `-AntlrJar`. The mutation helper uses native **character offsets**,
not line numbers; exact selected spans and snapshot/test hashes are in the
verification JSON. Keep raw artifacts outside Git and preserve unrelated main
work. Next priorities: PRS-REF-001, actual conditional/resource policy gaps,
expanded model-reference contracts, then explicitly qualified bridge extensions.
