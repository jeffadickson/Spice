# Bounded independent-source metadata and title handling

The shared importer accepts independent V/I source cards in the existing bounded
`[DC] value AC magnitude [phase-degrees]` form. For OP and constant-source
transient requests it validates inactive AC metadata and preserves the DC value.
For AC it preserves the DC bias separately from the complex excitation. Unknown
auxiliary clauses, duplicates and nonfinite parameters still fail. The previously
qualified AC-only SIN/DISTOF tail remains AC-only; this is not general waveform,
expression, measurement or multi-analysis support.

Every request-level analysis/source scan now excludes the first physical title
line, as the basic circuit parser already did. A source-shaped or directive-shaped
title is metadata. Corpus feature hints likewise count body analyses only.
Semicolon comments are stripped consistently before bounded metadata recognition.

Numeric convention: case-insensitive `a`/`A` is **atto, 1e-18**, not an input
ampere annotation when it directly follows the number. After an existing scale,
a trailing A does not change that scale: 2mA is 2e-3 and 1fA is 1e-15. `I1 out 0 AC 1` is outside the currently accepted full source
shape; use `I1 out 0 DC 0 AC 1` for a one-ampere AC current excitation.
`I1 out 0 DC 0 AC 1A` instead means 1e-18 ampere AC excitation. Output signal unit
`A` still means amperes. Typed JSON numeric fields retain their documented units.
Other arbitrary trailing unit text and scale forms are not implied.

The old AC path stripped A and could introduce an 18-order magnitude error.
An owned ngspice probe independently prints 1e-18 V for V1 ... AC 1A; dedicated
real-oracle tests assert nonzero relative magnitudes rather than relying on the
corpus absolute tolerance floor at tiny scales. This is finite evidence for the
named cases, not universal tiny-signal or numerical-range qualification.

Requirements: NGC-034, NGC-035, NGC-036 in ngspice-compatibility.sdoc. Verification:
../evidence/ngspice-source-metadata-verification.json and the newer corpus issue.
Windows GNU release regression and separate MSVC nightly instrumented coverage
have different compiler/profile scopes. Interrupted debug and budget-limited
mutation attempts remain explicit; no fresh Linux, full-soak or full-SPICE claim.
