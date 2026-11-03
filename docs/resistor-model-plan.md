# Generated resistor-model preparation

Approved bounded design: reuse generated passive parameters and a generated
`resistorModelProfile: identifier assignment* EOF` semantic entry. No second
lexer or textual splitting. PrimitiveSyntax retains model-reference candidates
and ordered typed assignments without broadening Supported scalar admission.
ResistorModelLowering.Prepare returns PreparedResistor with positive finite Ohms
and Scalar/ConstantModel/SheetModel provenance, or atomic diagnostics.

Native probes confirm numeric and .param values precede same-named models.
Existing ordinary scalar evaluation therefore runs before model lookup.
Model parameters evaluate in model.ScopeId; L/W evaluate in the actual retained
instance environment. Constant models allow only R; sheet models allow RSH and
optional finite SHORT/NARROW, require explicit positive L/W and positive finite
effective dimensions. Native negative etching is allowed when dimensions remain
valid. No model geometry defaults, multiplicity, temperature, noise, AC-specific
resistance or unknown terms are discarded. Unsupported terms remain refusals.

Primary source: ngspice e8285746c73a8229cf922487c8ccfcf6bea61bce,
res/restemp.c (effective geometry formula), res/ressetup.c (zero etch defaults).
Native name-collision/etch probes are retained under the resistor-6e51d0ae...
artifact directory; the main repository's unknown native resistor WIP is read
for context and remains untouched. Parent owns bridge integration.

- [x] Observe missing-helper TDD compilation failure before implementation.
- [x] Add generated metadata and scalar-precedence/model/geometry helper.
- [x] Test defaults, units, collision, malformed/unknown/duplicate terms,
      geometry boundaries, declaration scope and actual instance overrides.
- [x] Compare original 500/600/1200-ohm R/model cards with ngspice and existing
      typed elements for OP, complex AC and transient on both providers.
- [x] Complete frozen lexical model census, coverage, bounded mutations,
      full regression, real StrictDoc and evidence before final handoff.
