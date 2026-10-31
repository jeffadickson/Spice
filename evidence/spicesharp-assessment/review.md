# SpiceSharp assessment self-review

This is the implementing agent's self-review, not an external expert review.

- User direction explicitly retains Rust/drop-in solvers. Recommendation respects
  it and distinguishes a full analysis engine from a linear-provider interface.
- Parser-only acceptance is not conflated with simulation compatibility. Actual
  input, encoding and timeout counts total 910; no unfinished row is passing.
- All original analytical failures remain failures. RL direct API isolates the
  parser/export path; the diode's historical constants explain the discrepancy
  without silently loosening the original numerical acceptance bound.
- The initial multi-analysis oracle adapter failure is retained, with separate
  per-analysis oracle decks fixing assessment infrastructure rather than changing
  circuit equations. Negative cases are never numerical passes.
- Maintenance evidence separates default-branch commits, documentation, engine
  releases and generator releases; a recent timestamp is not production proof.
- Exact packages, lockfiles, declared licenses and source/binary hashes are kept.
  Bounded sample/diagnostic projection and original raw hash are explicit. The
  original harness source_sha256 key is a binary hash and is disclosed as such.
- Helper coverage is 205/212 lines and 132/168 branches. Separate property-body
  coverage is 13/13 lines and 2/2 branches. Renderer coverage is 98/99 lines and
  35/40 branches. None is upstream-library coverage or a mutation score.
- A 64-case FsCheck property, two malformed cases and actual disagreeing cases
  executed. Upstream mutation/fuzz/full regression, Linux, NativeAOT, GPU, large
  circuit performance and a new endurance run remain unexecuted.
- StrictDoc validation uses baseline requirements plus the new reuse document,
  explicitly excluding the unrelated uncommitted sheet draft: 15 documents,
  295 unique IDs, 283 parents. Requirements remain Draft. Sphinx also built the
  working engineering-doc tree; its local sheet draft is not shipped here.
- Desktop top and findings-section report renders were inspected. Generated
  evidence is HTML-encoded, the page is offline, and local links/anchors resolve.

No production engine migration, third-party source vendoring, CI enablement,
upstream outreach or user-work deletion was performed. The unfinished sheet-model
experiment remains local and unqualified. Reuse-before-build guidance is persisted
in the canonical personal file and reinforced in repository AGENTS.md.
