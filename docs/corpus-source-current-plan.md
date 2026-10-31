# Corpus source-current qualification plan

Intent: deepen the existing examples' evidence and remove the shared mutation
instrumentation blocker. Use the existing ProcessRunner, typed manifest,
ngspice executable, .NET numeric/JSON facilities and Stryker.NET. No dependency
addition or core simulator algorithm change is needed for the current oracle.

- [x] Reproduce the exact observation-preflight continuation mutation as CS0165;
  initialize the guarded local explicitly and run real OP/AC/transient and
  invalid-observation integration checks. Replay the native catch mutants;
  distinguish executable mutants from unavailable evidence. NGC-CORPUS-005.
- [ ] Add independent voltage-source current observations to the existing
  node-voltage profile. Reuse manifest IDs and units, require ngspice's exact
  named vname#branch alias, and enforce current-specific absolute tolerance
  1e-9 A plus the existing relative tolerance. Keep p-to-n positive sign.
  Store units and absolute bounds per signal; include the new bound in the
  inventory fingerprint. Policy v3 starts a new series.
- [ ] Test malformed/unknown identifiers, source-current table aliases, complex
  AC current and a resistor pair with identical node voltage but different
  analytical current. Run focused mutations, scoped line/branch coverage,
  real StrictDoc parsing, Sphinx and the actual tracked corpus.
- [ ] Publish the per-run issue and chart, retain raw artifacts locally, record
  limitations, review and push small verified checkpoints. CI stays undispatched.

Scope choice: independent voltage-source branch currents give a directly named
ngspice observable and independent Ohm/KCL checks. Arbitrary resistor/device,
controlled-source, inductor and nonlinear conduction/displacement current
profiles remain separate qualification work. We do not infer full SPICE or
model support from a passing current profile. Both providers must compare all
selected voltage/current signals; absent columns and wrong units fail visibly.

Additional discovered syntax gap: OP-only DC+AC source metadata is currently
rejected even though the same bounded source shape is accepted for AC. Retain
that diagnosis and add a draft requirement before a separate importer change.
