# Overnight endurance campaign

`make endurance` runs for up to five hours. The supervisor runs the selected
source mutations once, then spends the remaining budget on changing-seed batches:
parser/deck mutations, sparse-layout validation, analytic resistor dividers with
independent physical audits, and diode derivative checks. It stops on failure.
This is seeded fuzz/property testing, not coverage-guided libFuzzer or exhaustive
cargo-mutants. Repeating the same 22 source mutants is not counted as new breadth.

Commands from the repository root:

```powershell
make endurance                       # default 300-minute budget
make endurance MINUTES=30            # bounded shorter campaign
make endurance-smoke                 # 3 minutes, seed 1, mutations explicitly skipped
./scripts/Run-Endurance.ps1 -Minutes 300 -Background
```

On this host GNU make is at `C:/dev/tools/msys64/usr/bin/make.exe` if it is not
on PATH. The PowerShell entry point needs no make installation. It verifies the
pinned compiler and can recover the existing sibling portable Rust environment.
A cold build can consume a short smoke budget; that is recorded as incomplete,
not passing. Use a larger budget to warm the cache when necessary.

The worker takes an exclusive lock, preserves a source archive and hashes under
`work/endurance/<UTC-run-id>/repo`, and builds in a separate reusable workspace.
The source archive, source commit/dirty state, compiler/.NET versions and initial
seed identify the tested code. Snapshot acquisition rejects source edits racing
its reads. Later source edits do not affect the run. Shared build caches under
`target/` avoid rebuilding dependencies every night. Generated artifacts stay
ignored; campaigns do not update tracked qualification summaries or source.

`summary.json` is atomically refreshed after each completed batch. Counts cover
completed batches only. `pending_seed` identifies interrupted or failing work.
Budget exhaustion is distinct from success; mutation skip/timeout is explicit.
Build/mutation/failing-batch logs are retained with a 1 MiB-per-stream text limit
and truncation marker. Compiler and test concurrency is limited to two. Each fuzz
batch has a 60-second watchdog; cold builds and the mutation phase have their own
caps inside the total deadline. Mutation tests allow up to 900 seconds each in
this workflow, while ordinary Mutation.cs runs keep the 180-second default.

Failures retain `failure.json`, batch logs and replay seeds. Parser panics retain
original and bounded deletion-minimized input bytes (at most 256 deletion trials).
For replay, build the archived source and run:

```powershell
$env:SPICE_CAMPAIGN_SEED='<decimal replay seed>'
$env:SPICE_CAMPAIGN_CASES='1'
cargo test --locked --test endurance -- --ignored --exact seeded_batch --nocapture
```

The replay seed deterministically regenerates one case; it is not a random seed
chosen again at replay time. Non-parser failures retain seed-based replay without
claiming a minimized circuit. The batch test stays ignored in ordinary cargo test;
the overnight supervisor invokes it explicitly. Case sizes and generated numeric
domains are bounded. No OS-wide memory cap, coverage guidance or forced-termination
cleanup guarantee is claimed. A forcibly terminated supervisor leaves an unfinished
summary; inspect for remaining children before restarting. Archives/caches need
periodic disk-space review; no automatic deletion of failure evidence is configured.

The requested local schedule is midnight to approximately 5 a.m. in
America/Los_Angeles, with a morning results check. The machine must be available
for local execution. This five-hour duration is configured, not yet qualified by
a completed five-hour run. Early defects/timeouts stop the campaign for review.

StrictDoc contracts: END-001 and END-002 in docs/consistency.sdoc. Short-run,
coverage and negative-path verification is retained in evidence/endurance-qualification.md.
