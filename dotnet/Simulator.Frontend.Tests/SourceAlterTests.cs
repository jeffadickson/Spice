using Simulator.Syntax;
using Simulator.Client;
using Xunit;
namespace Simulator.Frontend.Tests;

/// <summary>Only post-OP source parameter state is admitted; dispatch remains immutable.</summary>
public sealed class SourceAlterTests
{
    [Fact]
    public void Post_OP_pulse_alter_retains_the_original_physical_request()
    {
        var plan=Prepare("op\nalter @v1[pulse] = [2 5 11n 12n 13n 54n 105n]\nlet after=@v1[pulse]");
        Assert.True(plan.Success,string.Join(",",plan.Diagnostics.Select(d=>d.Code)));
        var voltage=Assert.Single(Assert.Single(Assert.Single(plan.Operations).Requests).Circuit.Elements.OfType<Voltage>());
        Assert.Equal(.25,voltage.Volts);
        Assert.Contains(plan.ProgramOmissions,o=>o.Reason=="source_parameter_alter_executed_separately");
    }

    [Theory]
    [InlineData("alter @v1[pulse] = [1 2 3 4 5 6 7]\nop")]
    [InlineData("op\nalter @v1[pulse] = [1 2 3 4 5 6 7]\nop")]
    [InlineData("op\nif 1\nalter @v1[pulse] = [1 2 3 4 5 6 7]\nend")]
    [InlineData("op\nalter @missing[pulse] = [1 2 3 4 5 6 7]")]
    [InlineData("ac lin 1 1000 1000\nalter @v1[pulse] = [1 2 3 4 5 6 7]")]
    public void Unqualified_effects_refuse_a_partial_plan(string body)
    {var plan=Prepare(body);Assert.False(plan.Success);Assert.Empty(plan.Operations);}

    [Theory]
    [InlineData("PULSE(1 2 3 4 5 6)")]
    [InlineData("PULSE(1 2 3 4 5 6 7 8)")]
    [InlineData("AM(0 2 3 4 5 6 7)")]
    public void Only_seven_explicit_pulse_parameters_bind_as_a_target(string waveform)
    {
        var syntax=DeckParser.Parse("Shape\nV1 a 0 DC .25 "+waveform+"\n.control\nop\nalter @v1[pulse] = [1 2 3 4 5 6 7]\n.endc\n.end\n",collectDeclarations:true);
        var plan=RequestedAnalysis.Prepare(syntax);Assert.False(plan.Success);Assert.Empty(plan.Operations);
    }

    [Fact]
    public void A_child_source_is_not_a_root_parameter_target()
    {
        var syntax=DeckParser.Parse("Scope\nX1 a 0 src\n.subckt src p n\nV1 p n DC .25 PULSE(1 2 3 4 5 6 7)\n.ends src\n.control\nop\nalter @v1[pulse] = [1 2 3 4 5 6 7]\n.endc\n.end\n",collectDeclarations:true);
        var plan=RequestedAnalysis.Prepare(syntax);Assert.False(plan.Success);Assert.Empty(plan.Operations);
    }

    [Fact]
    public void Root_current_source_parameters_are_bound_without_a_voltage_branch_alias()
    {
        var syntax=DeckParser.Parse("Current source\nI1 a 0 DC .25 PULSE(1 2 3 4 5 6 7)\nR1 a 0 1k\n.control\nop\nalter @i1[pulse] = [1 2 3 4 5 6 7]\n.endc\n.end\n",collectDeclarations:true);
        var plan=RequestedAnalysis.Prepare(syntax);Assert.True(plan.Success,string.Join(",",plan.Diagnostics.Select(d=>d.Code)));
        Assert.Equal(.25,Assert.Single(Assert.Single(Assert.Single(plan.Operations).Requests).Circuit.Elements.OfType<Current>()).Amps);
    }
    private static RequestedAnalysisPlan Prepare(string commands)
        =>RequestedAnalysis.Prepare(DeckParser.Parse("Source state\nV1 a 0 DC .25 PULSE(-1 2 3m 4u 5u 6m 7m)\n.control\n"+commands+"\n.endc\n.end\n",collectDeclarations:true));
}
