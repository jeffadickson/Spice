# Bounded model checkpoint 434

All 100 implementation/qualification steps (335–434) now have concrete ledger
entries; final deep review/fixes 435 remains. Checkpoint source/test hashes cover
344 passing Rust tests, 29 managed conformance checks and 65 Windows xUnit cases.
The same 65 xUnit cases pass on Linux (17 seconds as reported by VSTest).

Iterations 425–433 add nonlinear explicit-initial KCL, composite-versus-parallel
element equivalence, a generated floating two-terminal BE residual/Jacobian
property (4096 requested cases, default seed 0x5350494345333535), explicit model
capabilities and unsupported-policy rejection, and a named diode RC transient
oracle. The transient maximum difference is 3.619e-5 V within the declared 1e-4 V
bound. Separate shared-time BE refinement, monotonicity and envelope checks pass;
UIC importer support and global error bounds are not claimed.

Fresh Rust coverage: 5310/5750 owned lines (92.35%); native branches unavailable.
The 77 LLVM warnings are identical to checkpoint 424's five attributed dependency
symbols; no owned/unclassified symbols. Exact owned Rust source/test hashes match
the Windows checkout. Coverlet: 166/174 owned client lines, 109/112 branches.
Separate helper metrics: AC 146/148 lines and 101/136 mapped branches; transient
141/144 lines and 120/162 mapped branches. Race/timeout paths, PowerShell branch
metrics, vendor internals and GPU remain unmeasured. These different scopes must
not be combined into one percentage.

Actual cargo-mutants initialization selection: three caught of three in
232.315 seconds, no survivor/unviable/timeout. Earlier charge selection remains
ten caught/four unviable of fourteen. A fresh standalone Stryker run
20261027T012716Z-e12a0f1b is still active at this checkpoint; no new managed
mutation counts are claimed here. It uses frozen native/fixture inputs and
will be reviewed with final evidence. Historical survivors remain historical.

Fourteen StrictDoc documents parse; 252 IDs are unique and 239 parent relations
resolve. The global check caught a new draft ID collision; the new snapshot
requirement is MUT-004, preserving existing MUT-003. Parsing alone does not detect
global duplicates or close requirements. Sphinx projects 230 XMLDoc entries with
warnings as errors. Named helpers reject oversized snapshots; an intentional
wrong AC request generates a failed report before the genuine comparison passes.

Raw artifacts remain local, no CI dispatch occurred, and no full SPICE/GPU or
production qualification is claimed. See the JSON reports for hashes and scope.
