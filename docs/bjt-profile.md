# Bounded BJT profile

Generated NPN/PNP model cards now compose native Ebers–Moll current paths and
real depletion/transport storage. This is the default GP subset with no
Early/high-injection/leakage/base-resistance/excess-phase couplings. Unsupported
terms refuse. The numerical runtime remains Rust with selectable LU/faer.

The native `junction_storage` element supplies Q and dQ/dV with exactly zero F.
Its IS is a transport-charge scale, never a fabricated zero-current model.
Separate BE/BC conduction senses feed CCCS transport; parallel storage bypasses
those senses, so displacement is not multiplied by beta. An outer zero-voltage
collector sense publishes signed total collector current. PNP reverses junction
and transport polarity. Diode-connected terminal aliases are valid.

Supported model terms: IS,BF,BR,NF,NR,CJE,VJE,MJE,CJC,VJC,MJC,FC,TF,TR;
TNOM must be 27 °C and LEVEL must be 1 when supplied. Defaults were checked against the
exact MIT SpiceSharp 3.2.3 model source: IS =1e-16 A, BF =100, BR =1, NF/NR =1,
CJE/CJC/TF/TR =0, VJE/VJC =.75 V, MJE/MJC =.33, FC =.5. Require positive IS/betas/emission
factors/potentials, nonnegative capacitances/transit times, 0<=grading/FC<1, and
finite representable derived junction parameters. Four-terminal/substrate models,
instance overrides, VAF/VAR, IKF/IKR, RB/RC/RE, leakage and other terms refuse.

The [owned example](../examples/bjt-ebers-moll.cir) provides nonzero CJE,CJC,TF,TR.
The exact public API path executed in the integration tests is:

```csharp
var syntax = DeckParser.Parse(deck, collectDeclarations: true);
var prepared = OperatingPointSelection.Select(syntax, backend: "faer").Prepared;
if (!prepared.Success) throw new InvalidOperationException(
    string.Join(",", prepared.Diagnostics.Select(d => d.Code)));
var request = prepared.Request! with {
    Analysis = new Ac([1e7], prepared.AcSources.ToArray())
};
var response = await new SimulationClient(existingEnginePath).RunAsync(request);
```

Raw API backward Euler starts from zero MNA state; configured BE starts at OP.
Charged explicit-initial-state/adaptive policies and nonlinear trapezoidal remain
unsupported. Nonlinear stored-energy checks remain NotChecked; BE KCL includes
actual charge differences. Source-model binding/readiness alone is not numerical
qualification.

Original ngspice47+ comparisons passed on both LU and faer for both polarities:
DC voltage/collector current, complex AC voltage/collector current, and10 ps BE
voltage/current at four declared times. The original50 ps grid missed its3 mV early
collector-voltage bound. Refining50 ps->10 ps->2 ps reduced the owned NPN2ns error
4.968 mV->.983 mV->.184 mV; that failed coarse result is retained. A separate Faer
complex residual refusal was fixed by the numerical-provider owner under the
unchanged acceptance, then the original circuit replay passed. These finite
results are not a global accuracy or full GP claim.

See bjt-profile.sdoc and evidence/bjt-profile-verification.json for source,
coverage, mutation dispositions and reproduction. The separate bounded
[MOS LEVEL1 profile](mos1-profile.md) is implemented; broader semiconductor
physics and intrinsic MOS charge remain open work.
