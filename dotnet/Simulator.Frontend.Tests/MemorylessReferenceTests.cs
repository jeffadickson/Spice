using System.Collections.Immutable;
using System.Text.Json;
using Simulator.Client;
using Simulator.Frontend;
using Simulator.RequestedQualification;
using Simulator.Syntax;
using Xunit;

namespace Simulator.Frontend.Tests;

public sealed class MemorylessReferenceTests
{
    [Theory]
    [InlineData(false,false,false,false)]
    [InlineData(true,false,false,false)]
    [InlineData(false,true,false,false)]
    [InlineData(false,false,true,false)]
    [InlineData(false,false,false,true)]
    public void Analytical_reference_checks_both_grids_and_preserves_strict_result(bool corruptEngine,bool corruptNative,bool unknownHash,bool missingObservable)
    {
        var syntax=DeckParser.Parse("phase\nV1 1 0 0 SIN(0 1 1k 0 0 45)\nR1 1 0 1k\nV2 2 0 0 PULSE(-1 1 0 10u 10u 500u 1m 45)\nR2 2 0 1k\n.tran 20u 2m\n.end\n",collectDeclarations:true);
        var plan=RequestedAnalysis.Prepare(syntax);
        Assert.True(plan.Success);
        var op=Assert.Single(plan.Operations);
        var circuit=plan.CircuitPreparation!;
        var signals=new[]{new SignalInfo("v(n1)","voltage","V"),new("v(n2)","voltage","V"),new("i(e0)","current","A"),new("i(e2)","current","A")};
        var engineTimes=new[]{0d,.000125,.002};
        var nativeTimes=new[]{0d,.00025,.002};
        double[] Values(double t,bool corrupt){
            var v=Math.Sin(2*Math.PI*1000*t+Math.PI/4);
            var pulse=t==0||t==.002?-1d:1d;
            return [v,pulse,corrupt?v/1000:-v/1000,-pulse/1000];
        }
        var manifest=new ResultManifest("1",op.Id,"succeeded",true,signals,engineTimes.Select(t=>new RealSample(t,Values(t,corruptEngine))).ToArray(),[],[],[],null,new("nalgebra_dense_lu_reference","cpu",128,false,false,true,false,"test"),"hash");
        var response=new SimulationResponse("1","succeeded",true,JsonSerializer.SerializeToElement(manifest,WireFormat.Options));
        var native=new NativeRaw(false,[new("time","time"),new("v(1)","voltage"),new("v(2)","voltage"),new("i(v1)","current"),new(missingObservable?"other":"i(v2)","current")],
            nativeTimes.Select(t=>new[]{t}.Concat(Values(t,corruptNative)).Select(v=>new ComplexValue(v,0)).ToArray()).ToImmutableArray()){PlotName="Transient Analysis"};
        var strict=RequestedComparison.Compare(op,circuit,[response],native);
        var observed=RequestedComparison.Compare(op,circuit,[response],native,unknownHash?"unknown":"2637631a2c3e35f3fedb22f2d3409537aef24e988e3e5e093482a3140b475ca2");
        Assert.Equal(missingObservable?"observable_gap":"numerical_disagreement",strict.Status);
        Assert.Equal(strict.Status,observed.Status);
        if(unknownHash){Assert.Null(observed.AnalyticalReference);return;}
        var reference=Assert.IsType<MemorylessReferenceResult>(observed.AnalyticalReference);
        if(missingObservable){Assert.Equal("observable_gap",reference.Status);Assert.False(reference.Agreed);return;}
        Assert.Equal(!corruptEngine&&!corruptNative,reference.Agreed);
        var current=Assert.Single(reference.Observables.Where(o=>o.NativeSignal.Equals("i(v1)",StringComparison.OrdinalIgnoreCase)));
        Assert.Equal(corruptEngine?3:0,current.Engine.Failed);
        Assert.Equal(corruptNative?3:0,current.Native.Failed);
    }
}
