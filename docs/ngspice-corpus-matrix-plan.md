# Corpus matrix implementation plan

Goal: account for every discovered upstream deck and learn missing capabilities
without treating an import, skip or missing reference as a numerical pass.
The user approved the matrix and one GitHub results issue per run, with burndown.

Approach: extend the existing NativeAOT-compatible benchmark helper, reusing its
bounded Process runner, typed public manifests and named-table comparisons.
A metadata-only inventory cannot establish execution. Running arbitrary upstream
control scripts would not preserve the read-only corpus boundary. Therefore the
real Rust importer examines every deck; only accepted declarative decks execute
in a local scratch directory, with explicit observation-only edits. No new
numerical/parser dependency or broad SPICE implementation is introduced.

- [ ] Expose the existing bounded library importer through --import-ngspice;
  regression-test successful translation, unsupported constructs and malformed input.
- [ ] Discover .cir/.sp/.ckt/.net/.deck/.spice/.inp and bounded extensionless
  decks with an .end record, skipping links; retain every candidate row, including
  resource rejection, importer diagnostics and budget-unfinished cases.
- [ ] Compare imported OP/AC/transient on LU/faer against named ngspice node
  voltages; retain exact input/request/executable/source hashes, selected points,
  tolerances, output edits, failures and local raw logs. Unsupported/risky control
  and external-resource decks are never executed by ngspice.
- [ ] Produce JSON, readable Markdown, prioritized diagnostic/feature groups,
  stable-corpus history and SVG burndown. Changed inventories form separate
  series; unsupported and unfinished cases remain remaining work.
- [ ] Publish one bounded GitHub issue per completed/partial run through the
  authenticated gh CLI, derived from origin. Explicit offline mode is available;
  publishing failures are recorded separately and never reported as uploaded.
- [ ] Execute corpus, meaningful negative/property/mutation and scoped coverage
  checks; parse real StrictDoc and build Sphinx; commit coherent checkpoints.

Limits: named node voltages and at most 200 representative coordinates per signal
are finite evidence; branch currents/global waveform error/vendor internals/full
SPICE are not qualified. Lexical feature hints accompany importer diagnostics;
their presence is not proof that each is the cause. No CI or main push inferred.
