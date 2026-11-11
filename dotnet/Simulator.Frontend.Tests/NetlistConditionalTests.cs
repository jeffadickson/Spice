using Simulator.Client;
using Simulator.Syntax;
using Xunit;

namespace Simulator.Frontend.Tests;

public sealed class NetlistConditionalTests
{
    [FsCheck.Xunit.Property(MaxTest=80, Replay="883,20261111")]
    public bool NonzeroConditionsSelectTheAnalyticalResistance(short value)
    {
        var plan=RequestedAnalysis.Prepare(DeckParser.Parse(
            $"Property\nV1 a 0 1\n.if ({value})\nR1 a 0 10\n.else\nR1 a 0 20\n.endif\n.op\n.end\n",
            collectDeclarations:true));
        return plan.Success && plan.Operations[0].Requests[0].Circuit.Elements.OfType<Resistor>().Single().Ohms
            == (value!=0 ? 10 : 20);
    }

    [Fact]
    public void BranchCurrentAliasFeedsOriginalStyleAssertionsAndCapturedReport()
    {
        var syntax=DeckParser.Parse("Report\nV1 a 0 1\nR1 a 0 300\n.control\nlet factor=1\nop\n"+
            "let out=-factor/v1#branch\nprint out > test.log\nif abs(out-300)>1e-9\nquit 1\nelse\nquit 0\nend\n.endc\n.end\n",
            collectDeclarations:true);
        var plan=RequestedAnalysis.Prepare(syntax);
        Assert.True(plan.Success,string.Join(",",plan.Diagnostics.Select(d=>d.Code)));
        var request=plan.Operations[0].Requests[0];
        var source=request.Circuit.Elements.OfType<Voltage>().Single();
        var manifest=new ResultManifest("1",request.CaseId,"succeeded",true,
            [new($"i({source.Id})","current","A")],[new(0,[-1d/300])],[],[],[],null,
            new("lu","cpu",128,false,false,true,false,"test"),"synthetic");
        var response=new SimulationResponse("1","succeeded",true,
            System.Text.Json.JsonSerializer.SerializeToElement(manifest,WireFormat.Options));
        var report=RequestedAnalysis.ExecuteTerminalReporting(syntax,plan,[response]);
        Assert.Equal(0,report.ExitCode);
        Assert.Equal("out = 3.000000e+02",report.Redirected["test.log"]);
    }

    [Theory]
    [InlineData(1, 10)]
    [InlineData(2, 20)]
    [InlineData(3, 300)]
    [InlineData(4, 40)]
    public void SelectsExactlyOneNestedBranchBeforeDuplicateDeviceChecks(int choice, double resistance)
    {
        var syntax = DeckParser.Parse($"Branches\n.param choice={choice}\nV1 a 0 1\n" +
            ".if (choice==1)\nR1 a 0 10\n.elseif (choice=2)\nR1 a 0 20\n" +
            ".elseif (choice==3)\n.if 0\nR1 a 0 missing\n.else\nR1 a 0 300\n.endif\n" +
            ".else\nR1 a 0 40\n.endif\n.op\n.end\n", collectDeclarations: true);
        var plan = RequestedAnalysis.Prepare(syntax);
        Assert.True(plan.Success, string.Join(",", plan.Diagnostics.Select(d => d.Code)));
        var request = Assert.Single(Assert.Single(plan.Operations).Requests);
        Assert.Equal(resistance, Assert.Single(request.Circuit.Elements.OfType<Resistor>()).Ohms);
        // Syntax recognition must keep all branches; selection belongs to execution preparation.
        Assert.Equal(5, syntax.Declarations!.Primitives.Count(p => p.Family == "R"));
    }

    [Theory]
    [InlineData(".else\nR1 a 0 1")]
    [InlineData(".if 1\nR1 a 0 1")]
    [InlineData(".if 0\n.else\n.elseif 1\n.endif")]
    [InlineData(".if missing\nR1 a 0 1\n.endif")]
    [InlineData(".if (1/0)\nR1 a 0 1\n.endif")]
    [InlineData(".if 1\n.else\n.else\n.endif")]
    [InlineData(".endif")]
    [InlineData(".if 1\nR1 a 0 1\n.else 2\n.endif")]
    [InlineData(".if 1 2\nR1 a 0 1\n.endif")]
    [InlineData(".if 1\n.include missing.lib\n.endif")]
    [InlineData(".subckt child a\n.if 1\nR1 a 0 1\n.endif\n.ends")]
    [InlineData(".if 1\n.param x=1\nR1 a 0 x\n.endif")]
    public void InvalidOrUnqualifiedConditionalsPublishNoPartialPlan(string cards)
    {
        var plan = RequestedAnalysis.Prepare(DeckParser.Parse(
            $"Branches\nV1 a 0 1\n{cards}\n.op\n.end\n", collectDeclarations: true));
        Assert.False(plan.Success);
        Assert.Empty(plan.Operations);
    }

    [Fact]
    public void DoesNotEvaluateUnreachableConditions()
    {
        var plan = RequestedAnalysis.Prepare(DeckParser.Parse(
            "Branches\nV1 a 0 1\n.if 1\nR1 a 0 10\n.elseif missing\nR1 a 0 20\n.else\n" +
            ".if (1/0)\nR1 a 0 30\n.endif\n.endif\n.op\n.end\n", collectDeclarations: true));
        Assert.True(plan.Success, string.Join(",", plan.Diagnostics.Select(d => d.Code)));
    }

    [Fact]
    public void RemovesInactiveAnalysesAndControlsWithTheirDevices()
    {
        var syntax=DeckParser.Parse("Branches\nV1 a 0 1\n.if 0\nR1 a 0 missing\n"+
            ".tran missing missing\n.control\nac lin 1 1 1\n.endc\n.else\nR1 a 0 10\n"+
            ".control\nop\necho selected\n.endc\n.endif\n.end\n",collectDeclarations:true);
        var plan=RequestedAnalysis.Prepare(syntax);
        Assert.True(plan.Success,string.Join(",",plan.Diagnostics.Select(d=>d.Code)));
        Assert.Equal("op",Assert.Single(plan.Operations).Kind);
    }

    [Theory]
    [InlineData(64,true)]
    [InlineData(65,false)]
    [InlineData(129,false)]
    public void BoundsStructuralWorkWithoutSilentlyTruncatingBranches(int depth,bool accepted)
    {
        var text="Budget\nV1 a 0 1\n"+string.Concat(Enumerable.Repeat(".if 1\n",depth))+
            "R1 a 0 10\n"+string.Concat(Enumerable.Repeat(".endif\n",depth))+".op\n.end\n";
        Assert.Equal(accepted,RequestedAnalysis.Prepare(DeckParser.Parse(text,collectDeclarations:true)).Success);
    }
}

