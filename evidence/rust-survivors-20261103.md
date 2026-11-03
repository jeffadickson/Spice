# November 3 overnight Rust survivors

Base: e679def; exact full revision, compiler/tool versions and source hashes are
in the adjacent provenance/hashes JSON. Original overnight source 8ecc0ab and
issue #30 are historical inputs; this replay uses the current base. All three
operators remain semantically identical (Gmin moved from line 261 to 276).

These are three real test gaps, with no production-code change required:

- Artifact `> 128` to `== 128`: reject 129/130/255 bytes before storage creation,
  accept 1/127/128; malformed alphabet, traversal and empty IDs are also checked.
- Switch CP minus CN to CP divided by CN: use finite nonzero CN and independently
  derived midpoint resistance sqrt(100*10000)=1000 ohm. Physical current/power
  balance passes under common-mode shifts and rejects a wrong returned state.
  The audit still shares the existing switch model law: this independently checks
  control wiring and midpoint physics, not a completely independent model oracle.
- Direct DC-audit Gmin `> 1` to `== 1`: admit 1 S and balance a 2 A supply at 2 V;
  reject nextafter(1,+infinity), 1.01, 2, zero, negative and nonfinite values.
  A deliberately inconsistent state must fail the audit.

Actual cargo-mutants 27.1.0 replay: 3 selected, 3 caught, 0 missed, 0 timeout,
0 unviable, 0 unattempted in the completed focused run. Baseline passed 4 tests.
Artifact mutation failed the boundary test; switch mutation failed deterministic
and property tests; Gmin mutation failed the direct-boundary test. Each mutant
compiled and failed with test exit 101. Adjacent mutation JSON retains phase times.
The 128-case proptest uses seed 2026110303; its injected-switch-mutant minimized
counterexample is common=0.3 V, output=1.8784053664992197 V, retained as replay hash
f9c28015bd0c5b78d08a2f188072a375d78f07f841444a763a9565ba776060d5.

Raw root: `E:/Artifacts/Spice-fix-20261103T140559Z/rust`.
The earlier `mutation` invocation was cancelled during baseline because trailing
cargo-test arguments did not restrict its build targets; zero candidates were
attempted there. The corrected `mutation-focused` invocation restricts build and
execution with cargo arguments. This is a three-candidate replay, not a new full
mutation score. No new coverage-guided fuzz campaign was run: new malformed and
negative inputs and fixed-seed property cases cover these changed test interfaces.
ExFAT incremental-cache hard-link warnings explicitly fall back to copying.

Reproduction (PowerShell; from an existing checkout; preserve unique output roots):

```powershell
$repo = (Get-Location).Path
$artifact = $env:SPICE_SURVIVOR_ARTIFACT_ROOT
if (-not (Test-Path -LiteralPath $repo -PathType Container)) { throw 'Missing checkout' }
if (-not (Test-Path -LiteralPath $artifact -PathType Container)) { throw 'Set existing unique artifact root' }
$env:CARGO_TARGET_DIR = Join-Path $artifact 'target'
$env:CARGO_BUILD_JOBS = '2'
cargo test --locked --test overnight_survivors_20261103
# Set SPICE_CARGO_MUTANTS to the existing pinned executable before replay.
if (-not (Test-Path -LiteralPath $env:SPICE_CARGO_MUTANTS -PathType Leaf)) { throw 'Missing pinned cargo-mutants executable' }
& $env:SPICE_CARGO_MUTANTS mutants --dir $repo --no-config --in-place `
  --timeout 180 --build-timeout 240 -C '--test' -C overnight_survivors_20261103 `
  -f 'src/artifact.rs' -f 'src/audit.rs' `
  -F '(src/artifact.rs:50:25: replace > with ==|src/audit.rs:180:55: replace - with /|src/audit.rs:276:60: replace > with ==)' `
  --output (Join-Path $artifact 'mutation-focused')
./scripts/Measure-RustIntegrationCoverage.ps1 -Repository $repo `
  -TargetDirectory $env:CARGO_TARGET_DIR -Output (Join-Path $artifact 'coverage') `
  -Test overnight_survivors_20261103
```

Real StrictDoc 0.30.4 gate parsed 46 documents, 397 unique requirement IDs and
372 resolved parents. The three new requirements remain Draft: bounded tests do
not close broad M1 qualification. No CI was dispatched, no remote run uploaded,
and no new platform, oracle or production-qualification claim is made.

Scoped LLVM coverage executed the same four tests successfully against the owned
instrumented library: artifact.rs 83/99 lines (83.84%); audit.rs 217/440 lines
(49.32%). The adjacent coverage JSON records every mapped owned file/hash, not
an aggregate suite coverage claim. Stable Rust supplied no measured branch maps;
branch coverage is unavailable. Vendor dependencies and test source are excluded;
other integration targets and main are outside this coverage run. These broad
file percentages include unrelated audit paths not exercised by this focused test.

