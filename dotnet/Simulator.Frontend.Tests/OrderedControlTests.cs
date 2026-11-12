using Simulator.Syntax;
using Xunit;

namespace Simulator.Frontend.Tests;

public sealed class OrderedControlTests
{
    internal const string Deck="""
        Ordered RC analysis
        Vin in 0 DC 5 AC 3
        R1 in out 1k ac=4k
        R2 out 0 4k
        C1 out 0 1n
        .control
        let success_count=0
        op
        let err=abs(v(out)/4-1)
        if err < 1e-12
          let success_count=success_count+1
        end
        ac lin 3 100k 200k
        let s=2*pi*i*frequency
        let gold=1.5/(1+s*2u)
        let err=abs(v(out)/gold-1)
        if vecmax(err) < 1e-12
          let success_count=success_count+1
        end
        pz in 0 out 0 vol pz
        let err=abs(pole(1)/-500000-1)
        if err < 1e-12
          let success_count=success_count+1
        end
        if success_count ne 3
          quit 1
        else
          quit 0
        end
        .endc
        .end
        """;

    [Fact]
    public void Every_original_analysis_is_retained_in_order()
    {
        var syntax=DeckParser.Parse(Deck,collectDeclarations:true);
        var plan=RequestedAnalysis.Prepare(syntax);
        Assert.True(plan.Success,string.Join(',',plan.Diagnostics.Select(d=>d.Code)));
        Assert.Equal(new[]{"op","ac","pz"},plan.Operations.Select(o=>o.Kind));
    }

    [Fact]
    public void Pz_oracle_projection_writes_the_complete_plot_without_all_alias()
    {
        var syntax=DeckParser.Parse(Deck,collectDeclarations:true);var plan=RequestedAnalysis.Prepare(syntax);
        Assert.True(plan.Success,string.Join(',',plan.Diagnostics.Select(d=>d.Code)));
        var operation=plan.Operations.Single(o=>o.Kind=="pz");
        var projection=Simulator.RequestedQualification.OriginalProjection.Create(Deck,syntax,operation,plan);
        Assert.Empty(projection.Instrumentation);Assert.False(projection.RequiresControlWrite);
        var projected=DeckParser.Parse(projection.Text,collectDeclarations:true);
        Assert.Empty(projected.Declarations!.ControlBlocks);
        Assert.Single(projected.Declarations.Cards.Where(c=>c.Name.Equals(".pz",StringComparison.OrdinalIgnoreCase)));
    }

    [Theory]
    [InlineData("op\nlet x=1\nop\nlet answer=x",1)]
    [InlineData("op\ndefine scale(x) 2*x\nop\nlet answer=scale(3)",6)]
    public void Interphase_scalar_reporting_is_executed_rather_than_omitted(string body,double expected)
    {
        var syntax=DeckParser.Parse("Interphase\nV1 a 0 1\nR1 a 0 1k\n.control\n"+body+"\n.endc\n.end\n",collectDeclarations:true);
        var plan=RequestedAnalysis.Prepare(syntax);Assert.True(plan.Success,string.Join(',',plan.Diagnostics.Select(d=>d.Code)));
        Assert.Equal(2,plan.Operations.Length);
        var plots=plan.Operations.ToDictionary(o=>o.ControlOrigin!.CommandRange.Start,
            _=>(IReadOnlyDictionary<string,System.Numerics.Complex[]>)new Dictionary<string,System.Numerics.Complex[]>());
        var reporting=ControlVectorExecution.ExecutePlots(syntax.Declarations!.ControlBlocks.Single().Commands,plots);
        Assert.Empty(reporting.Diagnostics);Assert.Equal(expected,reporting.Locals["answer"].Single().Real);
    }

    [Theory]
    [InlineData("missing")][InlineData("duplicate")][InlineData("unit")][InlineData("quantity")]
    public void Scalar_assertions_cannot_hide_incomplete_or_mistyped_physical_results(string corruption)
    {
        var syntax=DeckParser.Parse("Result integrity\nV1 a 0 1\nR1 a 0 1k\n.control\nop\nlet ok=1\nop\nif ok ne 1\nquit 1\nelse\nquit 0\nend\n.endc\n.end\n",collectDeclarations:true);
        var plan=RequestedAnalysis.Prepare(syntax);Assert.True(plan.Success);
        var signals=new[]{new Simulator.Client.SignalInfo("v(n1)","voltage","V"),new("i(e0)","current","A")};
        signals=corruption switch{
            "missing"=>[],"duplicate"=>[signals[0],signals[0],signals[1]],
            "unit"=>[signals[0] with{Unit="A"},signals[1]],
            _=>[signals[0] with{Quantity="current"},signals[1]]};
        var outcomes=plan.Operations.Select(operation=>{
            var manifest=new Simulator.Client.ResultManifest("1",operation.Requests[0].CaseId,"succeeded",true,
                signals,[new(0,new double[signals.Length])],[],[],[],null,
                new("nalgebra_dense_lu_reference","cpu",128,false,false,true,false,"test"),"hash");
            var response=new Simulator.Client.SimulationResponse("1","succeeded",true,
                System.Text.Json.JsonSerializer.SerializeToElement(manifest,Simulator.Client.WireFormat.Options));
            return new RequestedOperationResult(operation.Id,true,[new(0,response,null)]);
        }).ToArray();
        Assert.Throws<InvalidDataException>(()=>RequestedAnalysis.ExecuteOrderedReporting(syntax,plan,outcomes));
    }
}
