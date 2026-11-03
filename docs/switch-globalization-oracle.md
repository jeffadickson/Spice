# Fixed VSWITCH globalization oracle

The recovered randomized counterexample uses `RON=10`, `ROFF=10000`, reversed
thresholds `VON=0`, `VOFF=1`, control 0.5 V, drive 1.5822841689813791 V and a
100 ohm output load. At the exact midpoint its logarithmic resistance is the
geometric mean, 316.22776601683796 ohm. The independent divider gives
0.3801486345140583 V and source current -0.003801486345140583 A, where source
current is positive from its P terminal to its N terminal.

`scripts/Test-SwitchGlobalizationOracle.cs` runs an original VSWITCH deck through
ngspice47+ with `-n -D ngbehavior=psa -b`. It extracts exactly one finite `target`
and `branch` assignment and compares public typed `Voltage`/`PspiceSwitch`/resistor
OP requests through both CPU providers. The helper freezes the selected engine
once and checks identities after execution. It has no frontend/parser dependency.

The source-pinned repaired engine produced 0.38014863451405806 V and
-0.0038014863451405804 A through both LU and faer; ngspice and the analytical
calculation agree at the reported precision. Both solves took three iterations
and three linear solves, residual infinity norm 4.336808689942018e-19, without
damping, source continuation or Gmin. Full provenance, predeclared SI comparison
bounds and helper coverage are in [the evidence](../evidence/switch-globalization-oracle.json).

From the checkout, using an existing artifact parent and the explicitly selected
new engine and ngspice executable:

```powershell
if (-not (Test-Path -LiteralPath $artifactParent -PathType Container)) { throw 'Missing artifact parent' }
foreach ($file in @($engine, $ngspice)) {
    if (-not (Test-Path -LiteralPath $file -PathType Leaf)) { throw "Missing executable: $file" }
}
dotnet run --file scripts/Test-SwitchGlobalizationOracle.cs -- --self-test
dotnet run --file scripts/Test-SwitchGlobalizationOracle.cs -- $engine $ngspice (Join-Path $artifactParent 'fresh-switch-oracle')
```

This verifies one fixed OP. It does not update historical corpus 23/9 counts,
qualify arbitrary switch models or waveforms, or establish universal convergence.
