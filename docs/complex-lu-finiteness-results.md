# Complex LU component integrity

Base29ea45d. Production backend source is unchanged and byte-identical to the
frozen source for this file. Two new survivor counterparts concern independent
real/imaginary component checks at backend121 and136. Requirement source:
`complex-lu-finiteness.sdoc`. Machine evidence:
`evidence/complex-lu-finiteness-results.json`.

## Targeted evidence

The RHS contract separately supplies NaN and both infinities in either complex
component while the other staysfinite. Each invalid RHS must return InvalidInput
before solving, not become a later generic numerical failure. Empty RHS is also
refused with otherwise valid well-conditioned matrices.

For output integrity, scalar A=0.5 and finite b=(MAX,1) mathematically produce
(2*MAX,2): only the real component is unrepresentable. The executed output-guard
mutant returned Ok([Inf+2j]); the unchanged backend properly returns
NumericalFailure. The test also exercises imaginary and negative overflow on
the unmutated path. A mutant's first panic can stop its remaining cases.

The pinned nalgebra0.34.1 and num-complex0.4.6 arithmetic was source-reviewed:
division first multiplies by the finite0.5 pivot before dividing by0.25, so
one component really can overflow while the other remainsfinite. This is not
merely a synthetic error-category difference. A separate96-case Proptest oracle,
seed2026111910, uses bounded integer known solutions and exact quarter products
for A=0.5+0.25j. No production residual routine supplies expected answers.

The successful three-minute native replay caught both targeted mutants:2caught,
no missed, invalid or timed-out candidates. Cargo owned-package clean preceded
the replay; its baseline rebuilt the owned library and passed all three linked
positive/negative tests. Frozen and current diff/source hashes remain distinct,
although backend.rs itself is byte-identical. No filters or tolerances changed.

## Invalids and timeout remain separate

Nine newly completed backend Unviable cases were reviewed as RustE0277 compile
failures: unsupported Matrix/BackendCapabilities Default replacements and
unsupported vector/matrix division operators. Together with the six earlier
reviewed cases these explain15frozen compile-invalid outcomes, not15test kills.
This review is limited to the captured diagnostic/log hashes; newer arrivals
remain separate. No traits or filters were
added solely to make generated mutations compile.

The root-loop mutation backend227 changes parent[i]!=i to equality. Since a root
already satisfies parent[i]=i, its assignments do not progress and it loops.
Native compilation succeeded; tests exceeded the120-second limit (observed
120.0490541s), then native outcome Timeout was retained. Existing library tests
exercised the mutation. The campaign advanced afterward; a reviewed process
inventory found no old epoch native test executable remaining. This is an
intentional mutant hang, not evidence that the original solver is hung, a formal
termination proof, or a reason to relabel Timeout as Caught. Frozen timeout logs,
diff and phase results are in the raw checkpoint.

## Scope and next work

Three focused tests passed; full51unit+693integration pass,7ordinary skips.
Intel15 routine tests and separate UHD630 FP64 probe passed. Real StrictDoc
parsed118documents/640unique UIDs. Scoped owned backend coverage36/313lines;
stable Rust branch, test/vendor/main coverage unavailable. This is named-suite
coverage, not an overall trend or broad backend qualification.

Real-LU correction acceptance and exact polish boundaries remain pending,
including backend195 comparisons and266 EPS boundary. Backend271's changed-value
condition gates debug logging but not assignment; it needs diagnostic-policy
evidence rather than blanket numerical equivalence. Newly arriving DenseQr
acceptance comparisons remain pending too. These are not claimed caught or
equivalent by the complex-component checkpoint.

No fresh standalone fuzz, Stryker, ngspice, new original deck, performance,
formal/full SPICE or universal GPU claim. API128 error precedence and the earlier
Windows executable-removal cause remain open. Frozen4411 source/config/helper
and raw counts remain unchanged; the verified development checkpoint adds tests
and requirements only.

Raw root: `E:/Artifacts/Spice-complex-lu-20261119T090354Z`. All new build/cache,
snapshot/TEMP/TMP, coverage and logs stay on E:, with ordinary, replay, coverage
and live-epoch targets separate. No general memory caps/free-RAM gates, unrelated
jobs stopped, CI or main merge. Read-only review found no important numerical
oracle or component-isolation problem.
