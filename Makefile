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
