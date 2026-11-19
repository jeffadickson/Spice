# Fifty-deck expansion baseline

Base bc2ca0abfd3bb9f48c03728c40805f3540cde8dd, with the separately tested
qualification fail-fast guard. Pinned corpus 29c28a022625fd7cc2c56ead1cce2d3066663074.
The release engine is unchanged product behavior at the base revision, built
with intel-opencl; SHA-256 7ff8fc3cfe149e4d19191a21da2c7ed9ffb1c7881a14a65bd0e24b9f3247a8b3.
Managed sources and binaries, oracle and inventory hashes are retained in the
coordinator's summary. Rust 1.99.1, .NET SDK 10.0.404. No CI or main merge.

The 202-byte-deduplicated-original baseline completed in 381.48 seconds with
provenance_valid=true: 48 all-requested-analysis agreements, 42 executions without
complete qualification, three execution failures and 109 preparation refusals.
These 48 are existing capability, not increments for this request. New increments
at this checkpoint: zero of fifty. Earlier Chebyshev increments are not recounted.
Two SOI failures are unavailable native providers; the other execution failure is
retained separately. The pre-fix partial attempt is interrupted, not completed.

The largest preparation groups are physical directives (24), MOS profiles (17),
HFET/MESA/JFET profiles (12), requested programs (10), multiple behavioral controls
(seven) and unsupported active cards (six). These are first blockers, not promises
that one change completes every affected file. Several reference executions fail
on unsupported dialect constructs; those cannot establish agreement. Existing
full-deck RCA3040 agrees in AC but disagrees in DC and transient. Do not substitute
the separately qualified AC-only variant for its full original.

Runner correction: after a failed sweep point, retain its failure and explicitly
mark subsequent points unattempted_after_operation_failure. Continue later
operations and decks. The actual three-point singular-source regression failed
before the fix and passed afterward; a separate valid divider still agrees with
ngspice. This prevents thousands of repeated missing-provider launches without
turning missing points into passes.

Verification: 29 selected managed tests passed, zero failed/skipped. The first
coverage attempt did not discover its collector and supplies no coverage. A
separate explicit Coverlet 10.1.0 focused regression passed and measured the
RequestedQualification assembly at 253/641 lines and 285/1092 branches. This
single-test scope excludes other frontend/native code and is not repository-wide
coverage. Real StrictDoc SDReader parsed 130 documents. No new dedicated fuzz,
randomized campaign or mutation-framework score is claimed at this runner-only
checkpoint. Its observed red/green regression challenges the actual guard.

Raw evidence and reproduction scripts:
E:/Artifacts/Spice-next-fifty-20261119T233347Z/{Build.ps1,Baseline.ps1,Prepare.ps1,Test.ps1,
VerifyRunner.ps1,baseline-broad/summary.json,preparation.json,runner-focused-coverage}.
All bulk output and TEMP/TMP use E:. Ordinary memory is managed by Windows.
The active frozen mutation epoch, canonical main and unknown work are preserved.
