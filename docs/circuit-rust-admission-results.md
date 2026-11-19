# Direct Rust circuit admission checkpoint

Base `311742a`, November 19, 2026. Production `src/circuit.rs` is unchanged
and byte-identical to the frozen source. Six public-boundary tests and their
mutation-suite registration were added; no equation, tolerance or model changed.

JSON cannot represent NaN/infinity, so JSON rejection alone does not challenge
the public Rust circuit validators. New tests construct independently invalid
passive/source values, Early-effect controls, MOS1/bulk-BSIM3/FD-SOI reference
ports and junction models directly. Valid controls prevent blanket rejection.
The positive finite passive property uses64Proptest cases, seed2026111915,
covering decimal exponents -250 through249. These are admission checks, not
provider or semiconductor physics qualification. The standalone CCCS wire test
checks retention of negative, zero, fractional and nonunit offsets; it does not
assert that its referenced controlling branch exists.

Requirements: `CIRCUIT-RUST-ADMISSION-001`, `CIRCUIT-MODEL-PORTS-001`,
`CIRCUIT-OFFSET-WIRE-001` (Draft). Read-only simulated review found no important
issue in the field isolation, property domain, serialization scope or fixture.

Targeted native replay: **21/21 caught**, no missed, invalid or timed-out
candidates. Exact names and original/current diff mappings are retained. The
owned replay package was cleaned before invocation; its baseline rebuilt the
owned library and passed all6focused tests. These separate current-source kills
do not rewrite the frozen campaign's original outcomes or retest all4,411.

Ordinary qualification passed52unit+699integration tests,7ignored. Focused and
instrumented runs passed6tests each. The scoped circuit file measured80/194lines
(41.24%), excluding test/vendor source; branch coverage is unavailable. This is
not whole-engine, provider or runtime model coverage. Environment seed2026111916;
the positive passive property explicitly pins2026111915.

Intel routine15tests and the separate UHD630FP64/finite/shape/corruption probe
passed (driver31.0.101.2141, probe seed18117). This bounded routine check does not
qualify arbitrary decks or GPU kernels. StrictDoc parsed121documents/647UIDs.
No fresh ngspice comparison, standalone fuzz or Stryker campaign was run, and
there is no new original-corpus-deck count.

The initial focused compilation failed because Proptest's default stringified
diagnostic interpreted struct-literal braces as formatting syntax. Explicit
diagnostics corrected the test. The actual native snapshot was verified to
match the corrected test SHA289df31d45eb2a8781426edd9ebaaa87c812fb6937588e63b4b1bb9819875f1b
before candidate qualification; its fresh baseline passed6tests. The failed
normal draft and initial compile log remain preserved, not counted as mutant
outcomes or a product bug.

One original survivor replaces `fn unit_factor_offset()->f64 {1.}` with `1.0`:
the same exact typed constant, without side effects. Its raw Missed outcome and
diff remain; this is a reviewed source-equivalent disposition, not a kill or
separate executed replay. Three private native_mos accessor survivors remain
open. Four newer compile.rs Default replacements were reviewed as E0277 type
errors. One unary-negation deletion leaves a borrowed henries value where the
stamp requires f64 (E0308). All five are compile-invalid, not product bugs or
test kills; no Default traits or filters were added.

Raw native diffs, captured test sources, logs, coverage, review and identities:
`E:/Artifacts/Spice-circuit-admission-20261119T140601Z`.
Bounded evidence: `evidence/circuit-rust-admission-results.json`.
