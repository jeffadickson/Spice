# Sol original-deck campaign final handoff — 2026-11-13

The authorized campaign delivered five newly agreeing original decks. This final
wake adds no deck and no physical-model changes. Fresh replay on source
697bfea6f6a03c83589843215e4b77941403e5a3 passes all five on both LU and Faer:
ten deck/backend programs, fourteen requested analyses and 198 compared signal
points. All ten complete original control reports have exit code zero, no
diagnostics and `INFO: success`. All five unchanged full stock-ngspice programs
also exit zero and print success. This is bounded agreement, not general SPICE
compatibility or complete BJT qualification.

| Original under pinned tests/ngspice | Implementation commit | Qualified change | Requested analyses | Signal points per backend |
| --- | --- | --- | --- | --- |
| regression/model/special-names-1.cir | 60574becda726b928b415a597f09881e2d4c332d | Real vector echo; OP already agreed before the reporting fix | OP | 9 |
| regression/temper/temper-res-1.cir | e3fb8bf7ce9d8c66e1633ca54900d53b378c3ff4 | Physical resistor TC1/TC2/TCE scaling and reporting | Ten-point temperature DC | 60 |
| regression/misc/asrc-tc-1.cir | bd3bfb4bea057a3a1db2bb82accba970ee4723ea | Affine behavioral TC1/TC2, root Celsius and failure-only exits | OP, AC | 12 |
| regression/misc/alter-vec.cir | a888d81f6a45bd06ea966ded2ddda6fc2f8f78b7 | Post-OP pulse parameter metadata/reporting; no future-analysis alteration | OP | 2 |
| regression/misc/asrc-tc-2.cir | a1cd63ed3b2d359a9694a0e872a621de8827a197 | Native controlled resistance, exact derivatives and TC1/TC2 resistance scaling | OP, AC | 16 |

Original acceptance expressions and established observer bounds remain unchanged.
The comparator uses absolute 1e-6 V/1e-9 A plus relative 1e-4 as appropriate;
no startup samples were removed. Individual oracle projections separate other
control/reporting instructions, as recorded in raw omission lists. Complete
original reporting executes separately, and the independent stock program runs
use the unmodified original files. The final evidence retains these distinctions.
Stock stderr contains the known missing-spinit warning and, for alter-vec, the
expected note about using its DC value for OP; no missing-vector/error diagnostic
was hidden. The raw output is retained.

The final prerequisite commit 697bfea adds the native/API voltage-scaled CCCS law
`I = gain * Icontrol * (1 + k * Vcontrol)` with both exact partials, immutable branch
resolution, dense/sparse Newton and AC stamping, typed .NET wire/schema and
independent DC current audit. It is not a sixth original. Its authored OP and two
AC drives already agreed with stock ngspice on LU/Faer in 30 comparisons, maximum
absolute error 3.55e-15. BJT admission remains closed: pinned RSpice charge.rs
couples Early transport to TF/qb diffusion charge and a Vbc derivative. Dropping
that coupling would change the model.

Current runtime provenance:

- Native source: 697bfea6f6a03c83589843215e4b77941403e5a3; its native code was built
  before the verified source commit, with exact source hashes in the prerequisite
  evidence. The final handoff commit changes documentation/evidence only.
- Engine: E:/Artifacts/Spice-sol-earlytransport-a1cd63e-20261113T1110/target/debug/circuit-simulator.exe
- Engine SHA256: ccfa47b6b42ea066f56f3b33404fbf4c72731ded32ad35d45f949934b57c033a
- Pinned corpus/source: RSpice 29c28a022625fd7cc2c56ead1cce2d3066663074.
- Oracle: E:/Artifacts/Spice-RSpice-parity-20261108T054942Z/console-oracle/ngspice_con.exe
- Oracle SHA256: 12bc57018aa5142d28065704d09bc6b06da2a3886f7864ca042ed4824ccf910f.
- Final raw replay: E:/Artifacts/Spice-sol-final-697bfea-20261113T1210.

The latest implementation checkpoint ran 49 relevant native tests, one client
wire test, 926 frontend tests with 40 optional integrations skipped, two Pester
integrations and six positive/four negative schema validations. Two native manual
mutants and one DTO discriminator mutant were detected, then restored. Properties
ran 128 derivative/current cases with seed 202611131101 and 128 typed-boundary
fuzz cases with seed 202611131102; minimized counterexamples and other regression
seeds are retained. These checks belong to the preceding unchanged implementation
checkpoint; they were not rerun in this final documentation/replay wake. The final
wake reran both five-deck backends, five full stock programs and real StrictDoc
parsing of all 69 documents.

Latest measured coverage scopes remain explicit: focused native tests cover
1755/7902 owned lines, including 29/29 new model lines; stable Rust branch metrics
are unavailable. Focused managed client coverage is 8463/13909 lines and 279/840
branches. Full frontend/transitive coverage is 21845/27740 lines and 6500/10287
branches. Pester covers 131/141 commands (92.91%); its coarse JaCoCo mapping shows
54/54 lines while ten throw commands are unexecuted. Generated/transitive managed
code is included; scopes differ and cannot be compared as equivalent. No new
coverage collection ran for this documentation-only final checkpoint.

StrictDoc requirement IDs and evidence links are retained across the six slice
specifications. Their status remains Draft; finite verification does not promote
the larger architecture/work-package requirements. Exact IDs, test scope,
mutation outcomes, coverage, runtime/assembly/corpus hashes and commands are in
[evidence/sol-campaign-final.json](../evidence/sol-campaign-final.json), with
implementation detail in the five deck evidence files and
[evidence/voltage-scaled-cccs.json](../evidence/voltage-scaled-cccs.json).

Remaining work includes coupled BJT Early charge/history, PNP signs and base
resistance assumptions; rtlinv dialect/observer issues; behavioral resistance
transient, pole-zero and multiple independent controls; and nonlinear AC physical
audit without retained bias. Temper-2, RC and MOS memory stock-default mismatches
remain open. Tighter-control diagnostics are expressly uncounted. Temper-1/3
already passed baseline and are not new campaign results. Historical ac-resistance,
if-elseif and log-functions work is also not recounted. Normal Clippy reports the
same four unchanged findings; strict-clean Clippy is not claimed. Vendor C/C++,
Linux/GPU and full transient/model qualification remain unmeasured here.

No pushes, merges, CI, schematic edits, custom RAM caps or memory admission checks
were used. Jobs were serialized with owned process-tree timeouts and unique E
outputs. The ignored progress file records actual final worker/process shutdown;
`campaign_completed` means this authorized campaign ended, not full compatibility.
The campaign hard stop is 2026-11-13T12:49:29Z; no further implementation work is
scheduled within this worker after final shutdown.
