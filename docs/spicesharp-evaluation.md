# SpiceSharp evaluation and reproduction

Read the [detailed offline HTML assessment](assessments/spicesharp-evaluation.html).
The user explicitly retained Rust and drop-in solver selection. This is an isolated
reuse/reference assessment, not a production migration or parser replacement.
The canonical personal guidance now contains the reuse-before-build decision gate.

Pinned packages: SpiceSharp 3.2.3; SpiceSharp-Parser 3.4.1; transitive
SpiceSharpBehavioral 3.2.0 and Microsoft.CodeAnalysis.CSharp 4.12.0. The evaluation
projects retain NuGet lockfiles. All three declare/build on .NET 10; the external
libraries retain their upstream target frameworks. No third-party binaries are
committed. `evidence/spicesharp-assessment/` holds bounded results and source hashes.

## Scope and findings

- Parser-only: 910 attempted, 488 structurally accepted, 409 rejected, four
  encoding-rejected, seven input-unavailable and two worker timeouts. All parser
  processors are disabled: no includes, expansion, execution or semantic proof.
- Nine numerical groups: seven meet independent analytical bounds. Two malformed
  cases reject as expected. Multiple analyses execute in one candidate request.
- Ngspice: nine of eleven analysis instances meet the declared print-table bound;
  two disagree. The initial combined-table reference attempt is preserved; the
  corrected helper runs each owned analysis separately.
- RL current: parser IR(L1) returns zero; the direct engine's complex current
  matches the independent impedance equation. Pinned exporter source points to
  a real-property/DC export being used instead of complex-real data.
- Diode: 0.0663% difference from the current-SI target is explained by the pinned
  engine's older physical constants. The original bound failures are retained.
- A level-54 BSIM4 model explicitly rejects. One NPN and one MOS level-1 case pass;
  this is not full semiconductor qualification.
- FsCheck.Xunit 3.5.0: one property passed, 64 cases, replay `192,20261031`.
  Scoped helper and assertion-body coverage is recorded. No upstream mutation,
  coverage-guided fuzz, new soak, Linux, GPU, NativeAOT or scale benchmark ran.

## Reproduce from an existing checkout

Use the checked-out repository location on the current machine. Raw output parents
must already exist. Download the upstream corpus/reference tool using the existing
setup instructions; the assessment does not silently install or execute code from
corpus decks. The inventory's `corpus_root` must identify that local checkout.

```powershell
param([Parameter(Mandatory)][string]$Repository,
      [Parameter(Mandatory)][string]$Inventory,
      [Parameter(Mandatory)][string]$Ngspice)
$ErrorActionPreference='Stop'
if(!(Test-Path -LiteralPath $Repository -PathType Container)){throw 'Missing checkout'}
Set-Location -LiteralPath $Repository
if(!(Test-Path -LiteralPath work -PathType Container)){New-Item -ItemType Directory -Path work|Out-Null}
if(!(Test-Path -LiteralPath $Inventory -PathType Leaf)){throw 'Missing inventory'}
if(!(Test-Path -LiteralPath $Ngspice -PathType Leaf)){throw 'Missing ngspice executable'}
dotnet restore evaluation/SpiceSharp.Evaluation --locked-mode
if($LASTEXITCODE){throw 'Restore failed'}
dotnet build evaluation/SpiceSharp.Evaluation --no-restore
if($LASTEXITCODE){throw 'Build failed'}
dotnet evaluation/SpiceSharp.Evaluation/bin/Debug/net10.0/SpiceSharp.Evaluation.dll $Inventory work/spicesharp-evaluation.json
if($LASTEXITCODE){throw 'Campaign infrastructure failed'}
dotnet run --project evaluation/SpiceSharp.Reference -- work/spicesharp-evaluation.json $Ngspice work/spicesharp-reference.json
if($LASTEXITCODE){throw 'Reference infrastructure failed'}
dotnet run --project evaluation/SpiceSharp.DirectProbe
if($LASTEXITCODE){throw 'Direct probe failed'}
dotnet test evaluation/SpiceSharp.Evaluation.Tests
if($LASTEXITCODE){throw 'Property failed'}
```

Inspect recorded statuses and checks: campaign infrastructure exit zero does not
mean every circuit passed. Corpus worker wall timeout is three seconds and includes
startup; owned numerical workers have fifteen seconds. The parser loop has an
eight-minute budget; remaining rows are explicit `budget_unattempted`. The retained
run completed all rows. Raw/full output remains in ignored `work/`.

To regenerate the committed report from its bounded evidence:

```powershell
param([Parameter(Mandatory)][string]$Repository)
if(!(Test-Path -LiteralPath $Repository -PathType Container)){throw 'Missing checkout'}
Set-Location -LiteralPath $Repository
if(!(Test-Path -LiteralPath docs/assessments -PathType Container)){throw 'Missing report parent'}
dotnet run --file scripts/RenderSpiceSharpAssessment.cs -- evidence/spicesharp-assessment docs/assessments/spicesharp-evaluation.html
if($LASTEXITCODE){throw 'Report generation failed'}
```

The report is a dated evidence snapshot. When evaluating another package version,
refresh inputs, constants, code snapshots and judgments rather than merely changing
the version text. GitHub stores HTML source; download/open it locally to view the
offline report. No GitHub Pages deployment or CI enablement is required.

## Activity and evidence sources

- [Engine repository](https://github.com/SpiceSharp/SpiceSharp): default-branch code
  maintenance in May/June 2026; latest engine release 3.2.3 dated October 11, 2024.
- [Parser repository](https://github.com/SpiceSharp/SpiceSharpParser): latest observed
  default-branch commit October 6, 2026 is documentation; latest release 3.4.1 July
  26, 2026. These show activity, not guaranteed support or future release cadence.
- [Ngspice manual](https://ngspice.sourceforge.io/docs/ngspice-manual.pdf) and the
  separately executed ngspice-47+ binary remain independent reference evidence.

REUSE-001/002 remain Draft. Exact version metadata, binary/source hashes, original
report hash and bounded projection details are retained in the manifest.
