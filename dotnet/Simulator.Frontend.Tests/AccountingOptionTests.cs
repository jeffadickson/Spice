using Simulator.Syntax;
using Xunit;
namespace Simulator.Frontend.Tests;
public sealed class AccountingOptionTests
{
    [Theory]
    [InlineData(".options noacct")]
    [InlineData(".option NOINIT acct")]
    public void Selection_records_only_reporting_option_omissions(string card)
    {
        var syntax=DeckParser.Parse("Reporting\nV1 a 0 2\nR1 a 0 1k\n"+card+"\n.end\n",collectDeclarations:true);
        Assert.False(OperatingPointBridge.Lower(syntax).Success);
        var result=OperatingPointSelection.Select(syntax);
        Assert.True(result.Success);
        Assert.Contains(result.Omissions,o=>o.Reason=="reporting_options_not_selected");
    }
    [Theory]
    [InlineData(".option noacct reltol=1e-3")]
    [InlineData(".option noacct gmin=1e-3")]
    [InlineData(".option noinit bypass=1")]
    [InlineData(".option")]
    public void A_reporting_flag_does_not_hide_unknown_or_physical_options(string card)
    {
        var result=OperatingPointSelection.Select(DeckParser.Parse("Reporting\nV1 a 0 2\nR1 a 0 1k\n"+card+"\n.end\n",collectDeclarations:true));
        Assert.False(result.Success);Assert.Null(result.Prepared.Request);
    }
}