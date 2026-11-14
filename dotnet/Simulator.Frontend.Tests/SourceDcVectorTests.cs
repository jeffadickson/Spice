using System.Text.Json;
using Simulator.Client;
using Simulator.Syntax;
using Xunit;

namespace Simulator.Frontend.Tests;

/// <summary>Saved source settings are sampled at each executed DC coordinate.</summary>
public sealed class SourceDcVectorTests
{
    [Theory]
    [InlineData("V1 n 0 DC 7", "V1", "@v1[dc]")]
    [InlineData("I1 n 0 DC 7\nR1 n 0 1", "I1", "@i1[dc]")]
    public void Unsaved_parameter_query_retains_original_scalar_after_a_DC_sweep(string cards,string source,string vector)
    {
        var syntax=DeckParser.Parse($"Unsaved source query\n{cards}\n.control\ndc {source} 0 1 .25\nlet values={vector}\n.endc\n.end\n",collectDeclarations:true);
        var plan=RequestedAnalysis.Prepare(syntax);
        Assert.True(plan.Success);
        var operation=Assert.Single(plan.Operations);
        var report=RequestedAnalysis.ExecuteTerminalReporting(syntax,plan,operation.Requests.Select(Response).ToArray());
        Assert.Equal(7,Assert.Single(report.Locals["values"]));
    }

    [Theory]
    [InlineData("V1 n 0 DC 7", "V1", "@v1[dc]")]
    [InlineData("I1 n 0 DC 7\nR1 n 0 1", "I1", "@i1[dc]")]
    public void Swept_source_metadata_has_one_value_per_executed_point(string cards,string source,string vector)
    {
        var syntax=DeckParser.Parse($"Saved source settings\n{cards}\n.control\nsave {vector}\ndc {source} 0 1 .25\nlet foo={vector}\nlet middle=foo[2]\nlet direct={vector}[2]\nquit 0\n.endc\n.end\n",collectDeclarations:true);
        var plan=RequestedAnalysis.Prepare(syntax);
        Assert.True(plan.Success,string.Join(',',plan.Diagnostics.Select(d=>d.Code)));
        var operation=Assert.Single(plan.Operations);
        var responses=operation.Requests.Select(Response).ToArray();
        var report=RequestedAnalysis.ExecuteTerminalReporting(syntax,plan,responses);
        Assert.Empty(report.Diagnostics);
        Assert.Equal(new[]{0d,.25,.5,.75,1},report.Locals["foo"]);
        Assert.Equal(.5,Assert.Single(report.Locals["middle"]));
        Assert.Equal(.5,Assert.Single(report.Locals["direct"]));
    }

    [Theory]
    [InlineData("op",7)]
    [InlineData("dc V2 1 2 1",7)]
    public void Unswept_settings_remain_original_values_in_every_sample(string analysis,double expected)
    {
        var syntax=DeckParser.Parse($"Unswept setting\nV1 n 0 7\nV2 other 0 1\n.save @v1[dc]\n.control\n{analysis}\nlet values=@v1[dc]\n.endc\n.end\n",collectDeclarations:true);
        var plan=RequestedAnalysis.Prepare(syntax);
        Assert.True(plan.Success);
        var operation=Assert.Single(plan.Operations);
        var report=RequestedAnalysis.ExecuteTerminalReporting(syntax,plan,operation.Requests.Select(Response).ToArray());
        Assert.Equal(operation.Requests.Length,report.Locals["values"].Length);
        Assert.All(report.Locals["values"],value=>Assert.Equal(expected,value));
    }

    [Theory]
    [InlineData(false,"control_source_parameter_identity")]
    [InlineData(true,"control_source_parameter_nonfinite")]
    public void Corrupt_point_source_identity_or_value_is_refused(bool nonfinite,string expected)
    {
        var syntax=DeckParser.Parse("Invalid setting\nV1 n 0 7\n.control\nsave @v1[dc]\ndc V1 0 1 .5\nlet values=@v1[dc]\n.endc\n.end\n",collectDeclarations:true);
        var plan=RequestedAnalysis.Prepare(syntax);
        var operation=Assert.Single(plan.Operations);
        var request=operation.Requests[1];
        var source=Assert.Single(request.Circuit.Elements.OfType<Voltage>());
        var changed=nonfinite?(Element)(source with{Volts=double.NaN}):new Current(source.Id,source.P,source.N,1);
        request=request with{Circuit=request.Circuit with{Elements=[changed]}};
        operation=operation with{Requests=operation.Requests.SetItem(1,request)};
        plan=plan with{Operations=[operation]};
        var error=Assert.Throws<InvalidDataException>(()=>RequestedAnalysis.ExecuteTerminalReporting(syntax,plan,operation.Requests.Select(Response).ToArray()));
        Assert.Equal(expected,error.Message);
    }

    [FsCheck.Xunit.Property(MaxTest=128,Replay="987,2026111401")]
    public bool Saved_metadata_preserves_signed_sweep_coordinates(FsCheck.PositiveInt countInput,short startInput)
    {
        var count=2+countInput.Get%30;
        var start=(double)startInput;
        var end=start+count-1;
        var syntax=DeckParser.Parse(FormattableString.Invariant($"Random saved settings\nV1 n 0 7\n.control\nsave @v1[dc]\ndc V1 {start} {end} 1\nlet values=@v1[dc]\n.endc\n.end\n"),collectDeclarations:true);
        var plan=RequestedAnalysis.Prepare(syntax);
        if(!plan.Success)return false;
        var operation=plan.Operations.Single();
        var report=RequestedAnalysis.ExecuteTerminalReporting(syntax,plan,operation.Requests.Select(Response).ToArray());
        return report.Diagnostics.IsEmpty&&report.Locals["values"].SequenceEqual(Enumerable.Range(0,count).Select(i=>start+i));
    }

    [Theory]
    [InlineData("save @v1[dc]","",true)]
    [InlineData("save all","",false)]
    [InlineData("save @v2[dc]","",false)]
    [InlineData("","save @v1[dc]",false)]
    [InlineData("save @v2[dc] @v1[dc]","",true)]
    public void Only_explicit_pre_dispatch_control_save_selectors_capture_the_parameter(string before,string after,bool expected)
    {
        var syntax=DeckParser.Parse($"Capture order\nV1 n 0 7\nV2 other 0 9\n.control\n{before}\ndc V1 0 1 .5\n{after}\n.endc\n.end\n",collectDeclarations:true);
        var plan=RequestedAnalysis.Prepare(syntax);
        Assert.True(plan.Success,string.Join(',',plan.Diagnostics.Select(d=>d.Code)));
        var origin=Assert.Single(plan.Operations).ControlOrigin!;
        var captures=RequestedSourceDcCapture.Select(syntax,syntax.Declarations!.ControlBlocks.Single(),origin.CommandRange.Start);
        Assert.Equal(expected,captures.Contains("@V1[dc]"));
    }

    private static SimulationResponse Response(SimulationRequest request)
    {
        var manifest=new ResultManifest("1",request.CaseId,"succeeded",true,[],[new(0,[])],[],[],[],null,
            new("nalgebra_dense_lu_reference","cpu",128,false,false,true,false,"test"),"hash");
        return new("1","succeeded",true,JsonSerializer.SerializeToElement(manifest,WireFormat.Options));
    }
}
