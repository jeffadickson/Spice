using Simulator.Client;
using Simulator.Syntax;
using Xunit;

namespace Simulator.Frontend.Tests;

public sealed class DirectiveFollowupTests
{
    [Theory]
    [InlineData("numdgt=15",true)]
    [InlineData("numdgt=15 noacct",true)]
    [InlineData("numdgt=0",false)]
    [InlineData("numdgt=18",false)]
    [InlineData("numdgt=1.5",false)]
    [InlineData("numdgt=15 gmin=1e-9",false)]
    public void Decimal_report_precision_does_not_change_solver_input(string options,bool supported)
    {
        var syntax=DeckParser.Parse("Divider\nV1 in 0 2\nR1 in out 1k\nR2 out 0 1k\n.options "+options+"\n.op\n.end\n",collectDeclarations:true);
        var plan=RequestedAnalysis.Prepare(syntax);
        Assert.Equal(supported,plan.Success);
        if(supported)
            Assert.Contains(plan.Observations,o=>o.Reason=="reporting_options_not_selected");
    }

    [Theory]
    [InlineData("RS=0",true)]
    [InlineData("RS=1",false)]
    [InlineData("RS=-1",false)]
    [InlineData("RS=0 RS=0",false)]
    [InlineData("RS={0}",false)]
    public void Only_explicit_literal_zero_series_resistance_maps_to_the_existing_diode(string settings,bool supported)
    {
        var syntax=DeckParser.Parse("Diode\nV1 in 0 0 AC 1\nR1 in out 1k\nD1 out 0 dm\n.model dm D(IS=1u CJO=1u M=0 "+settings+")\n.ac lin 1 1k 1k\n.end\n",collectDeclarations:true);
        var plan=RequestedAnalysis.Prepare(syntax);
        Assert.Equal(supported,plan.Success);
        if(supported)
        {
            var diode=Assert.Single(Assert.Single(Assert.Single(plan.Operations).Requests).Circuit.Elements.OfType<SpiceDiode>());
            Assert.Equal(1e-6,diode.Model.JunctionFarads);
        }
    }
}
