# Download, qualify, and publish results from another computer

This workflow builds Spice from source; it is not a packaged GUI release. The
repository is private. A new machine needs PowerShell 7 and Git credentials with
permission to read the repository and push result branches. Administrator/sudo
approval may be needed once. The scripts cannot approve OS prompts or perform
your initial GitHub sign-in.

## Windows x64: first machine setup

From an existing Windows terminal:

    winget install --id Microsoft.PowerShell --exact --source winget
    winget install --id Git.Git --exact --source winget
    winget install --id GitHub.cli --exact --source winget

Open a fresh **PowerShell 7** terminal, authorize Git once, and start the workflow:

    gh auth login
    gh auth setup-git
    gh repo clone jeffadickson/Spice Spice-control
    Set-Location Spice-control
    ./scripts/Start-RemoteRun.ps1 -CheckoutPath "$env:USERPROFILE/Spice-qualification" -MachineLabel desktop01 -Minutes 300

The default command creates a separate owned checkout, installs/verifies tools,
runs Rust/.NET contract suites and StrictDoc parsing, executes the endurance budget,
attempts Rust coverage, and commits/pushes a bounded result report. Visual C++
Build Tools and the Windows SDK are installed when missing; approve the Microsoft
administrator prompt when Windows requests it. A canceled installer remains a
setup failure, not a successful linker installation.

## Ubuntu 24.04 x64: bootstrap PowerShell

These initial Linux-shell commands are the required exception before PowerShell
exists. They follow Microsoft's Ubuntu package-repository procedure:

    sudo apt-get update
    sudo apt-get install -y wget git gh
    wget -q https://packages.microsoft.com/config/ubuntu/24.04/packages-microsoft-prod.deb -O /tmp/packages-microsoft-prod.deb
    sudo dpkg -i /tmp/packages-microsoft-prod.deb
    sudo apt-get update
    sudo apt-get install -y powershell
    pwsh

Then use PowerShell:

    gh auth login
    gh auth setup-git
    gh repo clone jeffadickson/Spice Spice-control
    Set-Location Spice-control
    ./scripts/Start-RemoteRun.ps1 -CheckoutPath "$HOME/Spice-qualification" -MachineLabel ubuntu-lab -Minutes 300

The Linux setup supports apt-based Ubuntu/Debian x64. Native prerequisites use
apt when missing; prepared native tools avoid unnecessary sudo work. Rust/.NET
and documentation runtimes can be installed locally under the dedicated checkout's
ignored .tools directory. Other Linux package managers, ARM and macOS setup are
outside this profile. WSL evidence is identified as WSL, not bare-metal qualification.

Upstream instructions:
[PowerShell on Ubuntu](https://learn.microsoft.com/powershell/scripting/install/install-ubuntu),
[.NET install scripts](https://learn.microsoft.com/dotnet/core/tools/dotnet-install-script),
[Rust installation](https://doc.rust-lang.org/cargo/getting-started/installation.html).
Setup reads compiler/SDK pins from rust-toolchain.toml/global.json and pins
StrictDoc to 0.30.4. Installer URLs and acquired hashes go to work/setup/host.json;
rustup's executable is checked against its upstream SHA-256. Downloads and credentials
are not committed.

## Duration and reuse

The default endurance budget is 300 minutes. Positive finite values up to 1,440
minutes (24 hours) are supported. Setup, contract tests and coverage take additional
time outside that endurance budget.

    ./scripts/Start-RemoteRun.ps1 -CheckoutPath "$HOME/Spice-qualification" -MachineLabel ubuntu-lab -Minutes 600
    ./scripts/Start-RemoteRun.ps1 -CheckoutPath "$HOME/Spice-qualification" -Reference <commit-or-tag> -Minutes 60
    ./scripts/Setup-Host.ps1 -SkipNativeTools
    make endurance MINUTES=60
    ./scripts/Run-Endurance.ps1 -Minutes 60 -Background

Repeat runs reuse only a clean clone with the bootstrap's ownership marker and
matching origin. They fetch the chosen source reference and check out that exact
revision detached, preserving result branches. Arbitrary folders and local changes
are refused. Caches and archives stay in ignored work/target/.tools directories.
No global shell profile is edited. The existing local midnight schedule remains
300 minutes; a command-line duration change does not silently retime its 05:05 review.

For an existing GNU Windows development toolchain, -NoInstall -SkipNativeTools is
an explicit reuse mode, not a claim that MSVC is installed. Diagnostic options
-SkipMutation, -SkipCoverage and -NoPublish retain their omissions in the report.
A short budget can expire during cold compilation; zero cases are not a pass.

## Automatic Git publication

Branches are named:

    remote-runs/<os>-<architecture>-<machine-label>/<UTC-time>-<nonce>-<source-sha>

For example:

    remote-runs/linux-x64-ubuntu-lab/20261026T220000Z-a1b2c3d4-0123456789ab

Push access is checked before long testing. The publisher uses the explicit Spice
Qualification Runner commit identity, stages only manifest.json and checksums.json
under evidence/remote-runs, and pushes without force or merging into main.
The manifest is capped at 256 KiB. It records source identity, platform/architecture,
available tool versions, duration, seeds/counts, mutation/step outcomes, coverage
status and local-log hashes. A valid failed run is uploaded as failure evidence
with nonzero exit. Authentication or provenance failures stop publication with
local diagnostics.

Raw logs, absolute workspace paths, failure corpora, tools and binaries remain
local. Coverage unavailable/not_measured is distinct from measured zero. Reports
are reviewable evidence, not signed attestations or release approval. Successful
setup alone does not qualify the simulator.

From the control checkout, inspect uploaded results:

    git fetch origin
    git branch -r --list 'origin/remote-runs/*'
    git ls-tree -r --name-only origin/<remote-runs-branch> evidence/remote-runs
    git show 'origin/<remote-runs-branch>:<manifest-path>'

If upload is interrupted, the local result commit/branch is retained; push that
branch using ordinary Git. To import already completed endurance evidence from
the same clean, byte-identical source checkout:

    dotnet run --file scripts/RemoteQualification.cs -- --import-run <absolute-run-directory> --label recovery

Import mode is explicitly endurance_only_import: it does not claim the full
Rust/.NET/StrictDoc workflow was rerun. Hashes and source revision must match,
including checkout byte conventions. Never edit evidence to make it match.
Retain failed artifacts locally and promote minimized regressions through review.

Tooling checks:

These developer contracts currently require Pester 3.4.0; the launcher explicitly
selects it because Pester 5 changes assertion and discovery semantics. Verify the
installed version with `Get-Module -ListAvailable Pester` before running them.
They are separate from the standalone simulator qualification workflow; installing
a newer Pester alone does not validate these legacy contracts.

    ./scripts/Test-Remote.ps1
    dotnet run --file scripts/Test-RemoteMutations.cs

Measured scopes and actual host execution limits are recorded in
evidence/remote-workflow-qualification.md.

Generated mutation tools are installed/verified automatically unless SkipMutation
is selected. Endurance now runs targeted checks, a budgeted cargo-mutants/Stryker
phase, then seeded batches. Exported framework counts/hash/shard are distinct from
the legacy 22-mutation score. See [mutation-frameworks.md](mutation-frameworks.md)
for scope, zero-based shard replay, partial-run handling and native report locations.

New manifests use LF JSON structural newlines before hashing. Consumers must
verify stored Git blob bytes, not assume checkout text has identical newlines.
Five historical Windows result manifests from the October 27 review have
checksum mismatches consistent with CRLF-to-LF normalization; they remain
unchanged historical evidence and are not checksum-validated artifacts.

The campaign records UTC and monotonic elapsed time, its deadline, late completed
requested cases and temporary power-request status. Windows requests system-required
power for the campaign lifetime, then releases it; explicit sleep and OS policy can
override that request. Linux sleep prevention is unavailable. After wake, expired
work is stopped and late batches are excluded from completed counts. An overrun
above five seconds of cleanup allowance is `budget_overrun`, exits unsuccessfully
and is published as failure evidence. Import also checks elapsed time independently
for older producers. None of these fields establish active CPU testing time.
