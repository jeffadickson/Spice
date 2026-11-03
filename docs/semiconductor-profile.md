# Native semiconductor profile

The first qualified slice is `spice_diode`, alongside the unchanged `diode` and
constant-C `diode_with_capacitance` profiles. It adds voltage-dependent SPICE
junction depletion charge and TT diffusion charge, rather than interpreting CJO
as a constant capacitor. The native kernel is adapted from MIT SpiceSharp 3.2.3;
see [attribution](../third-party/ATTRIBUTION.md).

`model` uses effective SI parameters: `saturation_amps`, `thermal_volts` (N*kT/q),
`junction_farads` (CJO), `junction_volts` (VJ), `grading` (M),
`forward_coefficient` (FC) and `transit_seconds` (TT). Require IS,NVT,VJ>0;
CJO,TT>=0; 0<=M<1; 0<=FC<1. Frontend D models support IS,N,CJO/CJ0,VJ,M,FC,TT
and TNOM=27 at the default 27 C evaluation temperature. Unknown/duplicate/opaque
terms and other temperature, RS, BV or advanced model parameters refuse.

DC, biased AC and fixed backward Euler share the same current and charge laws.
`i_conduction(id)` excludes displacement. `i(id)` includes displacement in AC and
accepted-Q differences in BE. Raw BE starts from the documented zero MNA state;
configured BE initializes at operating point. Charged-junction configured
adaptive or explicit initial states currently refuse because their total-current
history/initial derivative semantics are not qualified. Nonlinear trapezoidal
already refuses. Physical KCL includes displacement; stored-energy audit is
explicitly NotChecked for nonlinear transport storage.

Run the [AC example](../examples/spice-diode-charge.request.json) using an existing
built engine and an existing artifact directory:

```powershell
param([Parameter(Mandatory)][string]$Repository,
      [Parameter(Mandatory)][string]$Engine,
      [Parameter(Mandatory)][string]$ArtifactDirectory)
if(!(Test-Path -LiteralPath $Repository -PathType Container)){throw 'Missing checkout'}
if(!(Test-Path -LiteralPath $Engine -PathType Leaf)){throw 'Missing engine'}
if(!(Test-Path -LiteralPath $ArtifactDirectory -PathType Container)){throw 'Missing artifact directory'}
Get-Content -LiteralPath (Join-Path $Repository 'examples/spice-diode-charge.request.json') -Raw |
    & $Engine | Set-Content -LiteralPath (Join-Path $ArtifactDirectory 'diode-result.json')
if($LASTEXITCODE){throw 'Simulation failed'}
```

Verification: `tests/spice_diode.rs` covers independent depletion integrals,
continuity, finite differences, a seeded128-case derivative property, malformed
parameters/wire data, total-current KCL and explicit unavailable-energy evidence.
`tests/spice_diode_oracle.rs` is ignored by default and must be explicitly invoked
with existing `SPICE_NGSPICE` and `SPICE_ORACLE_ARTIFACT_ROOT`. It executes owned
reference decks only, comparing DC voltage/current, complex AC voltage/current
and transient voltage/current on both LU and faer. It does not execute arbitrary
corpus control programs. See bounded evidence/semiconductor-diode-verification.json.

BJT and MOS families, broader diode parameters and temperature transformations
remain open scope. Thevenin/Spice21/OpenVAF assessments do not qualify those
models. See semiconductor-reuse-assessment.md and semiconductor-profile.sdoc.
