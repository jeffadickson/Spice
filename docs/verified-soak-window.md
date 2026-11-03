# Verified overnight checkpoint and window

The authorized next window is November2 at11pm through November3 at6am,
America/Los_Angeles. Request420 minutes, with an absolute finish time of
2026-11-03T14:00:00Z. Startup/scheduling does not establish a seven-hour result.
If startup is late, the effective test budget is shorter; requested and effective
budget seconds are separate fields. Five seconds of test cleanup is reserved
inside the window. Review the actual duration, cases, mutations and replay seeds.

`scripts/Run-VerifiedSoak.ps1` accepts a fresh clean linked checkout, canonical
repository, exact40-character commit, fresh artifact directory, configurable
minutes, optional round-trip FinishByUtc, and Background/ValidateOnly options.
It refuses dirty or mismatched source and another repository. The actual
Endurance worker derives the primary `.git` directory and holds the primary
repository's `work/endurance.lock` for its complete campaign. The outer shell's
provisional preparation lock is released before dispatch; an admission race can
refuse one run but cannot admit overlapping test workers. An already active run
is preserved. The worker records its canonical lock and supervisor assembly hash.

The launcher creates only new NTFS source-side work/target junctions to unique
E: output directories. It reuses existing pinned mutation binaries through a
tool-only junction and imports the installed Windows linker environment. It
moves/deletes no existing artifacts and shares no mutable build cache across
unrelated checkouts. The existing Endurance workflow snapshots source, runs the
maintained mutation frameworks once and repeats changing-seed Proptest batches.
The original midnight campaign uses the same canonical lock.

Whole-child supervision covers tool checks and host compilation as well as
actual tests. It awaits child exit or bounded deadline reconciliation, latching
the maximum UTC/monotonic elapsed time. External UTC corrections have no event
notification, so reconciliation wakes at most once per second; losing delay
waits are cancelled and observed. Exceptions after dispatch, including report
write failures, terminate and await the child tree. A clean worker return is
not itself a numerical pass; inspect the campaign summary's actual status.

The ignored canonical `work/verified-soak-active.json` identifies the pinned
checkout, PIDs, artifact directory and requested window. The artifact root has
launcher logs and launch-manifest.json; the real campaign summary is under its
`work/endurance/<run-id>/summary.json`. Preserve source/main and raw artifacts.
No result issue is warranted as a completed run before actual evidence exists;
the morning review should create one results issue per logical overnight run.

Verification before scheduling:16 Pester contracts passed, including actual
primary/linked worker-lock probes, held-lock refusal, a stuck bootstrap killed
by its outer deadline, clean source checks and an injected report failure after
dispatch. Pester measured203/338 commands (60.06%); branch coverage unavailable.
The inner BCL clock self-test exercises six split-clock/window contracts;
its scoped coverage is100% lines/61.76% branches. A primary lock probe measured
100% lines/55.55% branches in CampaignLock; these are small scopes, not whole
supervisor coverage. Windows system-required power acquisition/release ran.

The changing-seed property inventory now includes delayed-SIN phase holding
and smooth-switch midpoint divider checks against an analytical geometric-mean
resistance on both CPU providers. All18 methods passed256 requested cases each
(4608 requested cases), seed88320261103. Counterexample attempt counts are not
guessed. Focused native line coverage and hashes are in
`evidence/verified-soak-preflight.json`; Rust branch coverage is unavailable.

No real forced suspension was injected. Forced outer-supervisor termination can
leave bootstrap tools alive; actual test workers retain their canonical lock
and inner deadline. No Windows Job Object/forced-cleanup guarantee is claimed.
This Windows junction launcher and external separate git-dir layouts have no
Linux qualification; use the established native Linux workflow there. END-006
remains Draft until the actual overnight evidence is reviewed.

Scheduler registration is confirmed in evidence/overnight-schedule-20261102.json: one-time11pm launch and6:10am review are active; exact source pin8ecc0ab remains fixed. The recurring midnight300-minute campaign now uses clean committed-main checkouts and unique E: bulk directories, preserving any active run. Its5:05am review understands an active seven-hour run and new E: manifest pointers. Scheduling is not execution evidence.
