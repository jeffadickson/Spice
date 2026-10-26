# Endurance runner verification

The five-hour workflow is configured, not yet demonstrated by a completed
five-hour run. The exact source hashes, short-run outcomes, schedules and coverage
scopes are retained in endurance-qualification.json and endurance-coverage.json.
The original code is unchanged by each campaign; generated snapshots and inputs
remain under ignored work/ and target/ directories.

Verified behavior:

- GNU make expands and invokes the PowerShell target. A warm-cache three-minute
  smoke finished after 179.06 seconds with 745,472 cases, seed 1, mutations
  explicitly skipped. The initial cold-cache three-minute attempt exhausted its
  build budget and was correctly recorded as failed/incomplete with zero cases.
- The final hidden-background path, seed 42, completed 326,656 cases in 59.06
  seconds. A separate instrumented final-runner invocation completed 327,680 cases
  in 59.00 seconds. These two seed-42 runs overlap: do not add their counts as
  unique test inputs. The source archives identify each tested script version.
- A concurrent second supervisor was rejected by its exclusive lock. Invalid
  durations (including NaN/zero/oversized), malformed seeds and mutation timeout
  zero were rejected. Two Pester 3.4 launcher contract tests passed.
- The full Rust suite and Clippy with warnings denied passed. The ignored
  endurance test was separately invoked for 1,024 seed-1 cases. There is no
  coverage-guided fuzz or cargo-mutants installation on this host.
- The selected mutation campaign killed 22/22 mutants with the configurable
  target directory; see mutation.json. Its per-test limit now defaults to 180 s
  and permits up to 900 s for cold overnight builds, inside the outer campaign's
  total/phase deadlines. A full combined five-hour execution remains pending.
- A deliberately injected parser panic in a copied harness was detected, retained
  replay seed 0, and reduced to the single byte V by the bounded minimizer. The
  working harness hash was unchanged. This was a self-check, not a discovered
  product defect or an additional mutant in the 22/22 score.
- StrictDoc 0.30.4 parsed all 12 documents; 207 unique IDs and 188 parent links
  were checked. END-001/002/003 trace the runner, replay and scheduled workflow.

Coverage: dotnet-coverage measured 104/118 unique runner source lines (88.1356%)
and 60/88 mapped branches (68.1818%) for the successful instrumented short run
with mutations explicitly skipped. The raw Cobertura report and source hash are
retained. Mutation-phase, panic-minimization and forced-termination paths are not
covered by that runner measurement. Mutation.cs and PowerShell coverage are not
measured; their executed checks are described separately above. A fresh Rust
coverage attempt again stopped at missing MSVC link.exe, so all Rust line/branch
metrics, including the new endurance harness, remain unavailable.

Local thread automations are active for the user-selected midnight start and
05:05 review in America/Los_Angeles. Both saved configurations were read back.
The startup automation launches the hidden worker and verifies its artifacts;
the morning automation reports actual status, counts, mutation results, replay
seeds and log paths without treating a missing or unfinished run as a pass.
Local machine/app availability is required. No first overnight execution or
five-hour qualification result is claimed.
