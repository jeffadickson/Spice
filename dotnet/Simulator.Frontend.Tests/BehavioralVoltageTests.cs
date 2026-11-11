using System.Text.Json;
using Simulator.Client;
using Simulator.Syntax;
using Xunit;
using Simulator.RequestedQualification;

namespace Simulator.Frontend.Tests;

public sealed class BehavioralVoltageTests
{
    [Fact]
    public void Wire_envelope_can_serialize_the_native_depth_boundary()
    {
        BoundedExpression expression=new ExpressionInput();
        for(var depth=0;depth<64;depth++)expression=new ExpressionAdd(expression,new ExpressionConstant(0));
        var request=new SimulationRequest("depth",new(["0","in","out"],
            [new Voltage("V1",1,0,2.7),new BehavioralVoltage("B1",2,0,1,0,expression)]),new OperatingPoint());
        using var wire=JsonDocument.Parse(JsonSerializer.Serialize(request,WireFormat.Options),new(){MaxDepth=128});
        Assert.Equal("depth",wire.RootElement.GetProperty("case_id").GetString());
    }

    [Fact]
    public void Emitted_expression_depth_is_checked_after_log10_expansion()
    {
        var expression=string.Concat(Enumerable.Repeat("log10(",40))+"v(in)"+new string(')',40);
        var syntax=DeckParser.Parse($"depth\nV1 in 0 2.7\nB1 out 0 V={expression}\n.op\n.end\n",collectDeclarations:true);
        var plan=RequestedAnalysis.Prepare(syntax);
        Assert.False(plan.Success);Assert.Empty(plan.Operations);
        Assert.Contains(plan.Diagnostics,d=>d.Code=="expression_budget");
    }
    [Theory]
    [InlineData("ln","log")]
    [InlineData("log","log")]
    [InlineData("log10","divide")]
    [InlineData("exp","exp")]
    public void Requested_logarithmic_voltage_retains_a_typed_runtime_expression(string function,string op)
    {
        var syntax=DeckParser.Parse($"log\nV1 in 0 2.7\nB1 out 0 V={function}(v(in))\n.op\n.end\n",collectDeclarations:true);
        var plan=RequestedAnalysis.Prepare(syntax);
        Assert.True(plan.Success,string.Join(',',plan.Diagnostics.Select(d=>d.Code)));
        var request=Assert.Single(Assert.Single(plan.Operations).Requests);
        using var wire=JsonDocument.Parse(JsonSerializer.Serialize(request,WireFormat.Options));
        var element=Assert.Single(wire.RootElement.GetProperty("circuit").GetProperty("elements").EnumerateArray(),
            e=>e.GetProperty("kind").GetString()=="behavioral_voltage");
        var expression=element.GetProperty("expression");
        Assert.Equal(op,expression.GetProperty("op").GetString());
        if(op=="divide")expression=expression.GetProperty("left");
        Assert.Equal("input",expression.GetProperty("argument").GetProperty("op").GetString());
    }

    [Theory]
    [InlineData("ln(v(in)+v(other))","unsupported_behavior_control_count")]
    [InlineData("sin(v(in))","unsupported_behavior_function")]
    [InlineData("ln(time)","unsupported_behavior_runtime")]
    public void Unimplemented_model_semantics_publish_no_partial_plan(string expression,string diagnostic)
    {
        var syntax=DeckParser.Parse($"refuse\nV1 in 0 2.7\nV2 other 0 1\nB1 out 0 V={expression}\n.op\n.end\n",collectDeclarations:true);
        var plan=RequestedAnalysis.Prepare(syntax);
        Assert.False(plan.Success);Assert.Empty(plan.Operations);
        Assert.Contains(plan.Diagnostics,d=>d.Code==diagnostic);
    }

    [Theory]
    [InlineData(".ac lin 1 1 1")]
    [InlineData(".tran 1n 10n")]
    public void Dynamic_behavioral_analyses_remain_explicit_refusals(string analysis)
    {
        var syntax=DeckParser.Parse($"refuse\nV1 in 0 2.7\nB1 out 0 V=ln(v(in))\n{analysis}\n.end\n",collectDeclarations:true);
        var plan=RequestedAnalysis.Prepare(syntax);
        Assert.False(plan.Success);Assert.Empty(plan.Operations);
        Assert.Contains(plan.Diagnostics,d=>d.Code=="unsupported_behavioral_analysis");
    }

    [Fact]
    public void Legacy_selected_affine_profile_does_not_silently_change_contract()
    {
        var syntax=DeckParser.Parse("strict\nV1 in 0 2.7\nB1 out 0 V=ln(v(in))\n.op\n.end\n",collectDeclarations:true);
        Assert.False(OperatingPointSelection.Select(syntax).Prepared.Success);
        Assert.True(RequestedAnalysis.Prepare(syntax).Success);
    }

    [Fact]
    public void Behavioral_source_branch_currents_are_compared_and_cannot_be_ignored()
    {
        var syntax=DeckParser.Parse("branch\nV1 in 0 2.7\nB1 out 0 V=ln(v(in))\n.op\n.end\n",collectDeclarations:true);
        var plan=RequestedAnalysis.Prepare(syntax);Assert.True(plan.Success);
        var operation=Assert.Single(plan.Operations);
        var signals=new[]{new SignalInfo("v(n1)","voltage","V"),new("v(n2)","voltage","V"),new("i(e0)","current","A"),new("i(e1)","current","A")};
        var manifest=new ResultManifest("1",operation.Requests[0].CaseId,"succeeded",true,signals,
            [new(0,[2.7,0.9932517730102834,0,0])],[],[],[],null,
            new("nalgebra_dense_lu_reference","cpu",128,false,false,true,false,"test"),"hash");
        var response=new SimulationResponse("1","succeeded",true,JsonSerializer.SerializeToElement(manifest,WireFormat.Options));
        var raw=AsciiRaw.Read("Title: branch\nPlotname: Operating Point\nFlags: real\nNo. Variables: 4\nNo. Points: 1\nVariables:\n0 v(in) voltage\n1 v(out) voltage\n2 i(v1) current\n3 b1#branch current\nValues:\n0 2.7\n0.9932517730102834\n0\n0\n");
        var comparison=RequestedComparison.Compare(operation,plan.CircuitPreparation!,[response],raw);
        Assert.True(comparison.Agreed,string.Join(',',comparison.Gaps));
        Assert.Equal(4,comparison.Observables.Length);
        var bad=(ComplexValue[])raw.Points[0].Clone();bad[3]=new(0.1,0);
        Assert.False(RequestedComparison.Compare(operation,plan.CircuitPreparation!,[response],raw with{Points=[bad]}).Agreed);
    }
}
