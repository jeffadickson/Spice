# Ordered corpus qualification follow-up

User-authorized order: rollback, current comparisons, repeated analysis syntax,
then model families. Each checkpoint gets a separate verified commit and push.

1. Re-execute native Stryker on observation-preflight catch mutations against
current source; distinguish type-invalid mutations from collateral rollback.
Update stale backlog wording and record exact native categories.
2. Extend existing dimensional current comparisons to MNA branches L/E/H using
named i(device) and exact device#branch aliases. Add analytical RL/controlled-source
oracles and malformed identifier checks. Bump comparison policy to separate scope.
3. The recurring exactly-one-tran diagnostic often masks four/five-token syntax,
not repeated cards. Add zero-TSTART and explicit positive TMAX lowering; retain
explicit rejection of nonzero TSTART, UIC, duplicate analysis cards and AC+TRAN.
Validate each optional field, preserve simulation from t=0, and do not claim all
analyses were executed. Add requirement and analytical/negative/property tests.
4. Add a bounded passive resistor model family: .model name R (R=positive-value)
and Rdevice p n name. Reuse existing resistor solver semantics. Reject geometry,
temperature/noise/other parameters and cross-family duplicate model names.
Keep semiconductor families explicit unsupported; inventory model-kind counts
for future demand-driven model contracts rather than replacing devices.

Verification: release Rust regression, managed real ngspice tests, scoped native
mutation replays and coverage, real StrictDoc parsing and Sphinx. Recompare the
tracked corpus after source freezes; publish one results issue and chart per run.
No CI dispatch or broader SPICE/model-family qualification claim.
