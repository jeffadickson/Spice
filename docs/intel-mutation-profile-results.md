# Intel optional-profile mutation qualification

The frozen 4,411-candidate campaign did not enable `intel-opencl`; 83 selected
Intel candidates therefore lacked execution coverage. Their original Missed
outcomes remain historical. No configuration, source or helper in that campaign
was changed. Requirement `MUT-OPTIONAL-PROFILE-001` records the profile contract;
future campaigns still need explicit optional-provider lanes.

A separate replay used clean development source `d786a58`, a byte-identical
Intel module, exactly the 83 original names, and the actual Windows x64 native
provider SHA256 `4353e81a729f56f106212c8799b23af3e74a6b1d20ab21a00c335bcfb870e955`.
The enabled baseline passed 54 library, 6 CLI, 1 actual Intel contract and
8 provider tests. The replay finished in approximately 55 minutes:
**73 caught, 8 missed, 2 compile-invalid, 0 timeout**. Exit 2 records survivors;
it is not a successful all-killed result or infrastructure failure. The invalid
Default replacements require absent SimError/BackendCapabilities traits (E0277).
No artificial Default implementation or filter was added.

The eight survivors exposed test gaps, not a demonstrated production defect.
Tests now check small-scale relative residuals, the inclusive represented
1e-10 bound and its adjacent-ULP rejection, scalar complex embedding, independent
malformed dimensions/components, and stable experimental backend identity.
For the boundary witness, cancellation leaves h=1.0000000001000001e-10 and
scale 1+h; their represented quotient is exactly 1e-10. For the hardware scalar
fixture, (.5+.25j)*(2-3j)=1.75-j. No physics, solver algorithm or tolerance changed.
Requirements: `INTEL-RELATIVE-BOUNDARY-001`, `INTEL-COMPLEX-ADMISSION-001` (Draft).

The follow-up replay of the eight survivors freshly cleaned the owned package,
rebuilt the owned baseline and passed 56 library tests plus the actual Intel
contract. **8/8 caught**, no missed, invalid or timed-out case. The represented
boundary mutant was caught, not classified equivalent. Distinct dispositions
across this Intel group are **81 caught, 2 compile-invalid out of 83**; the two
attempts are not 91 distinct candidates. Source changes are tests only.

Default-feature regression: **52 unit and 693 integration tests passed, 7
ignored**, environment seed2026111914. The enabled follow-up used seed2026111913;
the initial83 used seed2026111912. StrictDoc parsed120documents/644uniqueUIDs.
The final routine Intel gate passed15tests, followed by the separate FP64
SpMV/finite/shape/corruption probe on UHD630, driver31.0.101.2141, seed18117.
No universal GPU execution or speedup claim follows from these bounded gates.

Scoped owned-gate coverage: **54/116 lines (46.55%)**, including module unit-test
lines; four instrumented gate tests pass. Branch coverage and native/GPU kernel,
vendor, main and other core coverage are unavailable in this measurement. The
actual hardware integration is separate from this instrumented safe gate.
The existing actual-Intel property retains 16 cases, seed2026111404. No new
standalone fuzz campaign, Stryker or ngspice corpus progress is claimed.

Read-only simulated review found no important issue in the exact equations,
boundary accumulation order or test scope. Frozen scores must not be replaced
by current-source retests or summed across the two enabled attempts.

Raw83 replay: `E:/Artifacts/Spice-intel-feature-replay-20261119T115640Z`.
Hardening/retest/coverage: `E:/Artifacts/Spice-intel-survivors-20261119T130253Z`.
Bounded evidence: `evidence/intel-mutation-profile-results.json`.
