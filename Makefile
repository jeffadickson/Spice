# The supervisor enforces the budget, snapshots source, and prevents overlap.
MINUTES ?= 300
.PHONY: endurance endurance-smoke
endurance:
	pwsh -NoProfile -File scripts/Run-Endurance.ps1 -Minutes $(MINUTES)
endurance-smoke:
	pwsh -NoProfile -File scripts/Run-Endurance.ps1 -Minutes 3 -Seed 1 -SkipMutation
