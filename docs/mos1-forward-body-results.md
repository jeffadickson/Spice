# MOS1 forward body bias and cutoff progress

MOS1 now supports forward effective body bias for DC and transient current
evaluation, using the clamped linear continuation of the body square root. Its
Newton derivatives are exact derivatives of that piecewise current law. AC uses
the separately named ngspice body-transconductance convention, and both sparse
conductance assembly and public channel-current reporting consume that convention.
The independent current audit reconstructs the threshold shift algebraically
without calling the production current/root/Jacobian helper. Meyer capacitances
use the same threshold continuation.

RSpice's `semiconductor.rs::mos1_threshold` and `classic/current.rs` were inspected
at source revision 29c28a022625fd7cc2c56ead1cce2d3066663074. The pinned ngspice
`mos1load.c` independently specifies the forward threshold and historical AC
slope. This is source-guided implementation plus actual ngspice execution;
RSpice itself was not executed for these new channel-isolation fixtures.

Cold inverse/clamped channel fixtures also exposed a shared numerical blocker.
With a cutoff channel, current-row reference magnitudes can still equal the
absolute tolerance while a trial first activates conduction. Twelve backtracking
candidates can stop immediately before activation. The controller now attempts
at most 24 candidates per correction. Residual/update tolerances and maximum
Newton correction/provider-call counts remain unchanged. Rejected transient
model trials emit optional structured `tracing` events with iteration, attempt,
timestep and reason; protocol stdout remains JSON.

The Windows native checkpoint passed 102 tests with no failures or skips:
25 library, 11 initial-Newton, 11 MOS1, 35 nonlinear, six PSpice switch,
five sparse AC circuit, seven sparse dynamic and two switch/PNP recovery tests.
The MOS property uses 128 cases and fixed seed `2026110202`, now including
positive body bias; other native properties retain their own configurations
with environment seed `2026111108`. No long fuzz or full mutation campaign ran.
A deliberate manual mutant that incorrectly reused the Newton slope only in
public AC current reporting was detected by the new AC regression. The source
was restored and all 102 named tests rerun successfully.

The committed `scripts/Qualify-Mos1ForwardBody.cs` ran 22 independent ngspice
reference fixtures on both LU and Faer. All 52 signal comparisons passed:
16 DC, 32 AC and four transient comparisons. Both polarities, normal/inverse
orientation, clamped continuation, body ramps and selected forward/inverse Meyer
gate/drain AC currents are covered. Maximum absolute current differences were
5.421010862427522e-20 A for DC, 6.535310652491911e-15 A for AC and
3.466171809616376e-11 A for transient. The reference fixtures explicitly use
IS=1e-300 to isolate the channel; they do not qualify a complete packaged MOSFET.
The helper validates case/provider identity, reference plot/grid/units, finite
values, immutable source/binary hashes during execution, and refuses reference
extrapolation. It snaps only sub-1e-20-second endpoint roundoff. Comparison
tolerances are declared in the JSON summary and were not loosened to make the
cold-start failures pass. Earlier 12-trial, helper-build and endpoint failures
remain local as failed/incomplete evidence.

LLVM coverage through the 11-test MOS1 integration suite measured 134/141 lines
(95.04%) in mos1.rs, 142/340 (41.76%) in api.rs, 246/526 (46.77%) in audit.rs and
434/1007 (43.10%) in newton.rs. Those are per-module counts under this specific
suite, not whole-project coverage or comparisons with earlier suites. Stable
Rust branch coverage is unavailable. The executable capability statement and
file-based C# qualification helper are not instrumented in that library scope.
All 58 StrictDoc documents were parsed using StrictDoc 0.30.4. Native tools were
Rust/Cargo 1.99.1; the managed helper used the pinned .NET 10 SDK and retained
PublishAot=false. C# Native AOT and Linux execution were not qualified here.

Raw evidence is under `E:/Artifacts/Spice-mos1-forward-66158d4`: `final` contains
the guarded regression/build logs, `oracles-final` the immutable reference
campaign, `coverage` the direct-LLVM report, and `engine-final.exe` the normal
native executable. Its SHA-256 is
3da26eea4d07fbb90e5cdf4420cebe9e2beb522c66cffde53690bb4be8c92950.
`OraclesFinal.ps1`, `Final.ps1` and `Coverage.ps1` retain exact commands and
explicit E: build/temp paths. Each ran under an 850 MiB aggregate process-tree
watchdog with 450 MiB startup headroom. The normal regression/build peaked at
815054848 bytes and coverage at 757927936 bytes. Vendor dependencies were reused;
the existing direct-LLVM helper instrumented only the owned library.

The original MOS memory case remains separate. Its 40 ns diagnostic failure was
explicit rejection of forward body bias. With the new model, the same physical
circuit completes an explicitly selected strict Faer/Gmin full window: 101
samples from zero to 2 microseconds, with successful physical audits. This
profile uses the default strict current tolerance rather than the deck's 1u
tolerance and explicitly selects Gmin startup. It is not the original default
requested execution. Against the original default-ngspice 150-point RAW, its
13 available mapped root/source signals do not all agree: only the four ideal
source-voltage signals pass over the full grid. Maximum voltage discrepancy is
3.4026492903471706e-3 V; maximum source-current discrepancy is
5.855648914196709e-4 A. No failed signal was excluded or tolerance widened.
The earlier separately tightened operating-point reference agrees at startup,
but it cannot replace the default full waveform comparison.

The unchanged default request still fails source continuation near scale
0.4188232421875. With explicit Gmin and the original 1u current tolerance,
independent KCL auditing remains stricter than the accepted initialization
residuals and refuses the result. Ordinary cold Newton initialization also
exhausts its 50-correction budget. Initialization/recovery policy, numerical
tolerance consistency, integration accuracy/defaults and missing default charge
parameters therefore need distinct investigation. Do not count the selected
successful window or these fixtures as a newly agreeing original corpus deck.
The hourly campaign's completed-and-agreeing original-deck count remains one.
No full SPICE, general intrinsic-charge transient, GPU or production
qualification is claimed.
