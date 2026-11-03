# Analog summer implementation plan

**Goal:** Prepare the measured scalar-voltage XSPICE summer subset using existing
VCVS series composition and a constant offset, without a new model kernel.

**Approved design:** ngspice e8285746c73a8229cf922487c8ccfcf6bea61bce
`summer/ifspec.ifs` and `summer/cfunc.mod` define
`out_gain * sum(in_gain[i] * (input[i] + in_offset[i])) + out_offset`.
Input is a vector of at least two ports; default type is voltage. Per-input
arrays default to zero offset and unity gain and have the input vector length.
Output gain/offset default to one/zero. Only voltage input/output semantics are
this slice. Event-domain real_gain, current and digital types remain refused.

Generated inspection found all 12 summer instances from the frozen 928 roots:
10 two-input cases with explicit matching arrays and two four-input cases with
omitted defaults. They occupy root scope in six example variants and mirrors.
Other analog/digital models still block those complete decks.

- [x] Write failing preparation/default/vector/length/type/parameter tests.
- [x] Extend existing generated port metadata with vector children; retain
      unsupported type markers and scalar gain admission behavior. Reuse the
      existing physical source capture for CR/LF/CRLF ranges.
- [x] Add a generated scalarVectorProfile over existing tokens; retain optional
      typed array members in ParameterSyntax without a custom splitter/parser.
- [x] Prepare ordered differential voltage controls, finite coefficients and
      offset in XspiceSummerLowering; evaluate model values in lexical snapshots.
- [x] Verify original A-card default/weighted/differential DC/complex AC/ramp
      cases against native ngspice and typed existing elements on both providers.
- [x] Execute bounded FsCheck, negative cases, coverage and meaningful mutations;
      re-inspect the 12 corpus cases, StrictDoc and docs, then commit evidence.

Parent owns OperatingPointBridge integration. No root bridge or native runtime
edits are planned. E: contains bulk artifacts; no CI or pushes.
