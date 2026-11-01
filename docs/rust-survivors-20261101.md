# November 1 native mutation follow-up

The scoped cohort is the fourteen missed mutants from overnight run
`20261101T065905Z-84e683c2`, Rust shard 2/16, based on `f7b14f5` plus
archived local changes. This follow-up adds test oracles; it does not change
production numerical or validation behavior. Exact dispositions and source hashes
are recorded in `evidence/rust-survivors-20261101.json`.

## Common causes and independent expectations

| Area | Gap in the previous evidence | New oracle |
|---|---|---|
| Artifact payload | Other invalid fields could mask an individual guard | Independently corrupt complete/status/schema with a correct digest |
| DC power | Failure status alone did not constrain relative tolerance | Signed discrepancy 0.5 W and absolute-power scale 1.5 W; exact 0.25 W tolerance |
| Sparse provider | Exercising a solver did not constrain its public name | Literal provider identity and capability identifier |
| Circuit validation | Combined bad inputs masked individual disjunctions | Separate node/element limits and nonfinite diode parameters |
| Initial state | A later solver failure could mask early validation | Invalid dynamic state must produce zero observer calls |
| Recording limit | Small samples did not test inclusive equality | Real 20-unknown, 100000-sample boundary = 2000000 scalars |
| Endpoint roundoff | Ordinary time scales hid a scaling error | Independently expected samples at 0, 1, 2, 3 ns |
| Expression recursion | Left operand depth did not constrain denominator depth | 70 nested denominators, below the separate node limit |
| Measurement | Range guard is redundant for the stable typed failure contract | Before/after endpoint rejection and inclusive endpoint values; separate invalid axis/value checks |
| Newton residual | Ordinary states hid the first voltage constraint boundary | 0.5 microvolt residual uses voltage-dimensional tolerance |
| Gmin | Typical interior values missed equality semantics | 1 S accepted, values above 1 S rejected |
| Waveforms | Later execution could reject invalid sources | Direct constant and isolated pulse-field validation |

Traceability: DEV-TEST-001/002, DEV-COV-001/002/003 and CONS-004.
The focused integration file is `tests/overnight_survivors_20261101.rs`;
the private norm oracle is `src/newton.rs::november1_contracts`.
No new simulation-support requirement or requirement closure is inferred from
mutation kills.

## Reproduction and limits

Import the repository's HostTools and MSVC environment. Use Cargo tests because
cargo-nextest was unavailable on this machine:

```powershell
cargo test --locked --release --lib --test overnight_survivors_20261101
$env:PROPTEST_RNG_SEED = '2026110101'
$env:PROPTEST_CASES = '256'
cargo test --locked --release
```

From the repository root, replay the exact saved cohort with:

```powershell
$root = (Get-Location).Path
if (!(Test-Path -LiteralPath $root -PathType Container)) { throw 'Missing checkout' }
. (Join-Path $root 'scripts/HostTools.ps1')
Import-SpiceTools -Root $root
Import-SpiceMsvc
dotnet run --file scripts/FrameworkMutations.cs -- --tool rust --minutes 45 --rust-test lib,overnight_survivors_20261101 --rust-names-file evidence/rust-survivors-20261101-names.json --rust-file 'src/{artifact,audit,backend_faer,circuit,configured,expression,measurement,newton,waveform}.rs' --workers 2
```
Keep the exact-name
inventory guard enabled: cargo-mutants field-deletion genres can bypass its
regular-expression filter. The first inventory attempt correctly rejected two
unrelated field deletions without executing them. Two early baseline attempts
failed on a malformed new fixture and executed no mutants; these are not kills.

The native framework freezes source before each replay. Source provenance is
therefore the recorded file hashes, not just the main-branch commit identifier.
Unrelated sheet-resistor work remains outside this commit. Raw logs and build
outputs remain local; large artifacts use the task directory on E:.

Coverage uses rustc instrument-coverage and matching stable MSVC LLVM tools,
following the existing focused coverage helper with its toolchain/output paths
adapted for this machine. It measures the named integration test's owned Rust
source maps, not the full regression suite, private unit test, .NET code, Linux,
GPU or full SPICE conformance. Branch coverage is unavailable in this run.
Library-generated proptest cases and existing malformed-input/fuzz smoke tests
are part of the full regression suite; this is not a new long-duration fuzz run.

The original 99 unviable and 19 unattempted candidates remain separate unavailable
evidence. This exact survivor replay does not rerun the whole shard or establish
a whole-project mutation score. Temporary-directory cleanup handles assertion
unwinding; forced process termination can still leave local artifacts.

## Reviewed equivalent survivor

`src/measurement.rs:17:33` changes the outside-range disjunction to a
conjunction. With validated finite strictly increasing samples, that conjunction
cannot hold. An outside request cannot match an exact sample or an interior
bracket, so it reaches the fallback `InvalidInput` return. Invalid axes, empty
samples and nonfinite input are rejected before this guard in both versions.
The error module explicitly documents contextual messages as nonstable API text.

This remains a raw missed mutant. It is equivalent only under the stable typed
error/no-extrapolation contract: context wording and additional scans differ.
No exact-message assertion, production change, tool exclusion or whole-score
adjustment is used to manufacture a kill. A separate source-only review confirmed
this reasoning, including singleton and signed-zero cases. New tests exercise
both sides of the range and inclusive endpoints.

## Verified outcome

Latest exact dispositions: 13 caught, one retained raw survivor with the scoped
contract-equivalence review above; no timeout, unviable or unattempted candidate
in this selected replay. Initial replay: 11 caught / 3 missed. Exact two-name
retry: power scale caught / measurement missed. Exact one-name retry: pulse
validation caught. Final regression: 459 passed / 1 explicitly ignored endurance
batch, seed 2026110101 and requested proptest cases 256. StrictDoc 0.30.4 parsed
19 documents with 312 unique IDs and 300 resolved parent links. Focused coverage:
1071 / 6211 owned Rust lines (17.24%); branch coverage unavailable. This is not
coverage for the full regression suite. Raw replay, regression and coverage logs
remain local with checksums in the bounded evidence.
