# The supervisor enforces the budget, snapshots source, and prevents overlap.
MINUTES ?= 300
.PHONY: endurance endurance-smoke mutation-tools mutation-frameworks mutation-rust mutation-dotnet
mutation-tools:
	pwsh -NoProfile -File scripts/Setup-MutationTools.ps1 -Install
mutation-frameworks:
	pwsh -NoProfile -File scripts/Run-FrameworkMutations.ps1 -Minutes $(MINUTES)
mutation-rust:
	pwsh -NoProfile -File scripts/Run-FrameworkMutations.ps1 -Minutes $(MINUTES) -Tool rust
mutation-dotnet:
	pwsh -NoProfile -File scripts/Run-FrameworkMutations.ps1 -Minutes $(MINUTES) -Tool dotnet
endurance:
	pwsh -NoProfile -File scripts/Run-Endurance.ps1 -Minutes $(MINUTES)
endurance-smoke:
	pwsh -NoProfile -File scripts/Run-Endurance.ps1 -Minutes 3 -Seed 1 -SkipMutation

# Supply an existing ngspice executable; the wrapper builds release/native helpers incrementally.
BENCH_ITERATIONS ?= 3

CORPUS_SECONDS ?= 1800
.PHONY: corpus-compare
corpus-compare:
	pwsh -NoProfile -File scripts/Run-CorpusComparison.ps1 -CorpusRoot "$(NGSPICE_CORPUS)" -NgspicePath "$(NGSPICE)" -BudgetSeconds $(CORPUS_SECONDS)
.PHONY: benchmark
benchmark:
	pwsh -NoProfile -File scripts/Run-ComparisonBenchmark.ps1 -NgspicePath "$(NGSPICE)" -Iterations $(BENCH_ITERATIONS)

# Run performance targets independently to avoid CPU/GPU timing contention.
.PHONY: parallel-test parallel-test-cpu parallel-test-intel parallel-test-launchers parallel-cpu parallel-intel parallel-cuda-preflight parallel-cuda
parallel-test: parallel-test-cpu parallel-test-intel intel-circuits parallel-test-launchers
parallel-test-cpu:
	pwsh -NoProfile -File scripts/Run-ParallelProbe.ps1 -Probe Cpu -TestOnly
parallel-test-intel:
	pwsh -NoProfile -File scripts/Run-ParallelProbe.ps1 -Probe Intel -TestOnly
parallel-test-launchers:
	pwsh -NoProfile -Command "Invoke-Pester -Configuration @{Run=@{Path=@('scripts/tests/ParallelProbe.Tests.ps1','scripts/tests/CuDssProbe.Tests.ps1','scripts/tests/IntelCircuits.Tests.ps1','scripts/tests/IntelReplay.Tests.ps1');Exit=[bool]1}}"
parallel-cpu:
	pwsh -NoProfile -File scripts/Run-ParallelProbe.ps1 -Probe Cpu
parallel-intel:
	pwsh -NoProfile -File scripts/Run-ParallelProbe.ps1 -Probe Intel
.PHONY: intel-circuits
intel-circuits:
	pwsh -NoProfile -File scripts/Test-IntelCircuits.ps1
parallel-cuda-preflight:
	pwsh -NoProfile -File scripts/Run-CuDssProbe.ps1 -PreflightOnly
parallel-cuda:
	pwsh -NoProfile -File scripts/Run-CuDssProbe.ps1
