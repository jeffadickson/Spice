# Affine B-source helper: measured subset

The helper prepares voltage/current B sources as finite affine relations over
node voltages. It reuses generated ANTLR recognition, scalar ParameterEvaluator
semantics and existing typed VCVS/VCCS/independent sources. It adds no native
expression interpreter, nonlinear model kernel or dependency. The parent
simulation worktree owns actual bridge integration; these helper results are
separate from its complete-circuit execution census.

## Reuse and contract

The parent authorized generated header decomposition and an affine subset after
the measured gain slice. Existing full ngspice and SpiceSharp evaluations remain
the broader reuse assessment. For this bounded native subset, existing runtime
controlled sources express the required relations exactly; new runtime algebra
is unnecessary. Constant subtrees reuse the established parameter evaluator.
Only voltage-dependent affine composition is new domain glue.

Primary source inspection used ngspice `e8285746c73a8229cf922487c8ccfcf6bea61bce`,
particularly [ASRC loading](https://github.com/ngspice/ngspice/blob/e8285746c73a8229cf922487c8ccfcf6bea61bce/src/spicelib/devices/asrc/asrcload.c).
Independent original B cards supply the numerical reference. No upstream source
or assets are redistributed.

`catalog.BehavioralSources` retains source name, scope, output P/N, voltage/current
quantity, expression syntax and support state. `BehavioralSourceLowering.Prepare`
returns an atomic typed relation: offset plus finite per-node voltage terms.
Voltage coefficients are dimensionless; current coefficients are siemens.
Offsets are volts or amperes according to the output quantity.

A generated `behaviorHeaderProfile` decomposes the existing composite token by
reusing SpiceLexer BODY mode with `SemanticHeaderOnly=true`. Ordinary deck
recognition leaves that flag false. No handwritten header scanner or alternate
parser is introduced. Expression derivation uses the generated expression AST.

Admitted operations are sums/differences, unary signs, multiplication by a
voltage-independent scalar, and division by a nonzero scalar. `v(a,b)` preserves
its differential sign. Node terms coalesce case-insensitively and ground `0`
is removed. Limits are 4096 UTF-16 characters, 1024 affine evaluations, depth 64
and 16 distinct node terms by default. Conditions/comparisons, runtime identifiers,
current references, nonlinear products/divisors, unknown functions, quoted node
references and opaque B parameters refuse atomically. This is a finite subset;
constant scalar support follows the existing evaluator.

## Fresh evidence

- Pure helper suite: 27 passed, including 128 FsCheck cases with explicit
  `Check.One` and valid `Rnd(20261103)` replay metadata. Tests cover scalar
  algebra, negative cases, caller overrides, budgets and physical ranges.
- Full isolated frontend suite: 186 passed, 11 native-environment skips, 197 total.
- Existing syntax suite: 406 passed, 1 explicit exFAT symbolic-link skip. An initial
  C: run failed for missing symbolic-link privilege; an E: run with forward-slash
  environment paths hit existing path-string assertions. The final run used
  canonical Windows paths via `Path.GetFullPath`; no product behavior was changed
  to suppress those environment failures.
- Original B cards: 78 voltage comparisons passed on LU/faer over voltage,
  current and constant profiles in DC, 100 Hz complex AC and linear-ramp transient.
  The voltage AC reference is `2-1.5j`, exercising a phased second control. The
  current profile obeys independent load KCL `Vout=-Vin-1.5` with 1 kOhm resistance.
  These tests prepare coefficients with the helper and independently emit typed
  requests; the parent separately tests its actual bridge emission.
- Coverlet 10.1.0 measured the affine evaluator 96/96 lines, 117/132 branches;
  generated-header extraction 21/22 and 24/30; frontend adapter 8/8 and 12/12.
  Defensive header recovery and some guard alternatives remain unmeasured.
  Generated ANTLR code is excluded; no whole-runtime coverage claim is made.
- Stryker.NET 5.0.0 killed both selected Combine-offset arithmetic mutants.
  Of 1000 generated mutants, 811 were excluded by the span filter and 187 had
  compile errors outside that selected span. This is a two-mutant check. The
  first analysis failed because the test project referenced Syntax transitively;
  the disposable E: snapshot added an explicit reference to that same existing
  project. Production dependencies are unchanged.

A new CR-only continuation regression first failed at physical line 3 instead of 4.
The fix reuses the existing `DeckParser.PhysicalPositions` map (internal visibility)
for expression subranges. LF, CRLF and bare-CR tests now preserve line and column.
ANTLR's LF-only line counter is not used as a physical source map.

## Frozen corpus profile

The 928-root lexical census checked root hashes and expanded 890 inputs;
38 resource refusals remained and no root bytes changed. It observed 245 lexical
B instances in 60 roots: 224 voltage and 21 current. 87 instances in 10 roots prepared
under lexical defaults (85 voltage, 2 current). Other instance outcomes were:
29 non-affine, 22 invalid parameter environments, 6 unsupported behavioral syntax,
6 missing parameters, 6 opaque headers, 1 runtime reference and 88 unsupported scalar
expressions. Repeated includes and inactive definitions are included. These
counts are not complete solved circuits, active-instance overrides or new solver
passes; other blockers still apply. The profile predates the physical-range-only
fix and has no admission-semantic difference from it.

Raw data and exact source hashes are in `evidence/affine-behavior-results.json`;
artifacts remain under `E:/Artifacts/spice/behavior-xspice-nov2/affine-b`.
StrictDoc validation passed 24 documents, 346 unique IDs and 335 resolved parents.
`SEM-BEHAVIOR-AFFINE-001` remains Draft. No independent new fuzz harness ran;
fixed malformed/negative cases and the bounded library-based property campaign
are the new input checks.
