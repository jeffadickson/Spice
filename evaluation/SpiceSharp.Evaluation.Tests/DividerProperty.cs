using FsCheck;
using FsCheck.Xunit;
using SpiceSharp;
using SpiceSharp.Components;
using SpiceSharp.Simulations;
using Xunit;

public sealed class DividerProperty
{
    /// <summary>Challenge the pinned engine with varied signs and resistor ratios.</summary>
    [Property(MaxTest=64,Replay="192,20261031")]
    public void DirectEngineMatchesIndependentDividerLaws(PositiveInt a,PositiveInt b,int source)
    {
        // Bounded physical values keep this a resistance/sign property rather
        // than an unqualified extreme-range or solver-conditioning benchmark.
        var r1=100d+a.Get%100000;var r2=100d+b.Get%100000;var volts=(source%10001)/1000d;
        var circuit=new Circuit(new VoltageSource("V1","in","0",volts),new Resistor("R1","in","out",r1),new Resistor("R2","out","0",r2));
        var op=new OP("property");var voltage=new RealVoltageExport(op,"out");var current=new RealCurrentExport(op,"V1");
        var points=0;
        foreach(var flag in op.Run(circuit))
        {
            points++;
            Assert.InRange(voltage.Value,volts*r2/(r1+r2)-1e-9,volts*r2/(r1+r2)+1e-9);
            Assert.InRange(current.Value,-volts/(r1+r2)-1e-11,-volts/(r1+r2)+1e-11);
        }
        Assert.Equal(1,points);
    }
}
