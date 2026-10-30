# Five-area final review

Reviewed range: `4eb2644..7e4c8f4`. Reviewer: a fresh read-only Codex
`gpt-6-astra` agent, not an external human expert. Source and evidence were read
in passes; the reviewer did not edit the checkout or launch a new campaign.

The review found no demonstrated numerical or transport regression. It reran
the existing compiled audit binary (11 passed), independently totaled the retained
full Rust run (422 passed, zero failed, one ignored), inspected managed 82/82 and
benchmark helper 29/29, checked all latest audit coverage source hashes without
mismatches, and inspected all five passing NativeAOT benchmark cases.

## Important finding and disposition

CONS-007 still mandated that all AC/configured physical audits be NotChecked,
contradicting the implemented profiles and AUD-AC-001/AUD-CONFIG-001.
MODEL-007's comment repeated the obsolete scope. This is an important
requirements/acceptance conflict, despite the newer introductory status note.

Fixed while preserving the requirement IDs: supported linear AC and fixed
configured profiles are now explicit, with unavailable nonlinear-bias/adaptive
history profiles and global accuracy remaining NotChecked. The model-charge
comment, current status table and historical handoff gap were reconciled.
This correction changes no numerical or transport code. The prior integration
RED log `work/five-area/audits-full-final.log` demonstrates that the old
all-AC-NotChecked expectation fails; its corrected discriminating test is green
in the retained 422-test run. Real StrictDoc parsing and Sphinx were rerun after
the documentation correction; see `five-area-final-requirements.json` and
`engineering-docs.json`. No second review was requested.

## Deferred minor

`docs/benchmarks.md` says executable/source bytes are hashed and rechecked.
The actual ending rechecks cover decks, requests and both child executables;
other source files and the suite manifest are hashed during setup. No concurrent
source modification was demonstrated in the retained runs, so this does not
invalidate their observed comparisons. Clarifying that wording is deferred;
the broader ending source-recheck claim must not be inferred from it.

## Rulings and remaining qualification

- Use the requested main checkout and local ignored evidence convention without
  another worktree/approval flow; no push or CI was inferred for this task.
  Cost if wrong: reduced isolation; the work is retained in small reversible
  commits and the checkout was clean before and after the checkpoints.
- Put owned ngspice pairs beside benchmark requests in `compat/benchmarks`
  rather than duplicate a reference protocol under `compat/ngspice`.
  Cost if wrong: those pairs are discovered through the benchmark inventory,
  not the separate legacy fixture inventory.
- Use installed nightly MSVC for focused coverage after GNU lacked its profiler
  runtime and disk/WSL failures prevented Linux execution. Cost if wrong:
  compiler/platform-specific behavior remains unmeasured; mapped-line scopes,
  source hashes and nullable branch coverage remain explicit.
- The reviewer declined arbitrary forced-parent-death/OS tree containment.
  Ruling: it remains excluded; bounded startup-exception evidence is sufficient
  for this slice, not a containment guarantee. Cost if wrong: workers outside
  the exercised ownership profile can require stronger operating-system controls.
- The reviewer declined fresh Linux numerical correctness/coverage. Ruling:
  leave it unavailable and retain prior reports as historical. Cost if wrong:
  current Linux differences remain undiscovered until the environment recovers.
- The reviewer declined whole-product mutation closure, universal convergence
  and global waveform accuracy. Ruling: no such claim is made; selected finite
  evidence stands. Cost if wrong: untested circuits, survivors and unmeasured
  paths can still fail and require further qualification.
- Retain the local `work/five-area` ledger/raw logs rather than delete the skill
  workspace: the user's raw-evidence preservation instruction takes precedence.
  Cost: local disk use. Only verified inactive rebuildable caches were cleaned;
  source and raw evidence were preserved.

The original managed startup recheck remains load-sensitive: one busy run hit
its five-second budget, then the focused two-case probe and full 82-test run
passed without a source change. It is not a latency guarantee or a hidden pass.
Client and benchmark Stryker survivors/invalid/uncovered results remain separate
from the fully caught selected Rust changes. Full M1, SPICE, GPU and production
qualification are not established.

After the correction, the complete Rust suite was rerun: 422 passed, zero failed, one ignored (work/five-area/final-review-rust.log). StrictDoc parsed 14 documents with 275 stable IDs and 263 resolved parents; Sphinx rebuilt with warnings as errors. Product, test and fixture files are unchanged from the reviewed 7e4c8f4 checkpoint.
