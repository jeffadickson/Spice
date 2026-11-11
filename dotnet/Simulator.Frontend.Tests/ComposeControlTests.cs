using Simulator.Syntax;
using Xunit;

namespace Simulator.Frontend.Tests;

public sealed class ComposeControlTests
{
    [Fact]
    public void Scalar_composition_is_admitted_only_in_the_terminal_reporting_region()
    {
        var syntax=DeckParser.Parse("compose\nV1 1 0 2.7\n.control\nop\ncompose gold values 1 2 3\nlet error=vecmax(abs(gold-gold))\nif error\nquit 1\nelse\nquit 0\nend\n.endc\n.end\n",collectDeclarations:true);
        var plan=RequestedAnalysis.Prepare(syntax);
        Assert.True(plan.Success,string.Join(',',plan.Diagnostics.Select(d=>d.Code)));
        Assert.Single(plan.Operations);
    }

    [Theory]
    [InlineData("compose time values 1")]
    [InlineData("compose frequency values 1")]
    [InlineData("compose all values 1")]
    public void Reserved_result_names_are_refused(string command)
    {
        var syntax=DeckParser.Parse("compose\nV1 1 0 2.7\n.control\nop\n"+command+"\n.endc\n.end\n",collectDeclarations:true);
        var plan=RequestedAnalysis.Prepare(syntax);
        Assert.False(plan.Success);
        Assert.Empty(plan.Operations);
    }
}
