using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Nodes;
using Simulator.Client;
using Simulator.Frontend;
using Simulator.RequestedQualification;
using Simulator.Syntax;
using Xunit;
namespace Simulator.Frontend.Tests;

public sealed class ReferenceGridResponseTests
{
    [Theory][InlineData(true,true)][InlineData(false,true)][InlineData(true,false)]
    public void Numerically_matching_interpolated_data_is_not_exact_grid_evidence(bool correctGrid,bool correctMode)
    {
        var plan=RequestedAnalysis.Prepare(DeckParser.Parse("grid\nV1 a 0 1\nR1 a 0 2\n.tran .5 1 0 .5\n.end\n",collectDeclarations:true));
        Assert.True(plan.Success);var operation=Assert.Single(plan.Operations);
        var raw=new NativeRaw(false,[new("time","time"),new("v(a)","voltage"),new("i(v1)","current")],
            new[]{0d,0.5,1}.Select(t=>new[]{new ComplexValue(t,0),new(1,0),new(-0.5,0)}).ToImmutableArray()){PlotName="Transient Analysis"};
        var replay=ReferenceGridReplay.Prepare(operation.Requests[0],raw);
        var times=correctGrid?new[]{0d,0.5,1}:new[]{0d,0.25,0.5,1};
        var manifest=new ResultManifest("1",replay.CaseId,"succeeded",true,[new("v(n1)","voltage","V"),new("i(e0)","current","A")],
            times.Select(t=>new RealSample(t,[1,-0.5])).ToArray(),[],[],[],null,new("nalgebra_dense_lu_reference","cpu",256,false,false,true,false,"test"),"hash");
        var payload=JsonSerializer.SerializeToNode(manifest,WireFormat.Options)!;
        payload["integration"]=new JsonObject{{"time_control",correctMode?"accepted_time_grid":"adaptive"}};
        var response=new SimulationResponse("1","succeeded",true,JsonSerializer.SerializeToElement(payload));
        // A constant waveform agrees even when the old generic comparator
        // interpolates across an extra point; the new grid gate must reject it.
        Assert.True(RequestedComparison.Compare(operation with{Requests=[replay]},plan.CircuitPreparation!,[response],raw).Agreed);
        Assert.Equal(correctGrid&&correctMode,ReferenceGridReplay.HasExactGrid(replay,response));
        Assert.False(ReferenceGridReplay.HasExactGrid(replay,new("1","succeeded",true,JsonSerializer.SerializeToElement<object?>(null))));
    }
}
