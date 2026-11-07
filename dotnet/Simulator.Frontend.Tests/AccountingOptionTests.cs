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
    [Theory]
    [InlineData(".option TEMP = 27.0")]
    [InlineData(".options acct temp=+2.7e1")]
    [InlineData(".options filetype=ascii")]
    [InlineData(".options filetype=binary noacct")]
    public void Requested_analysis_accepts_explicit_default_temperature_and_output_encoding(string card)
    {
        var syntax=DeckParser.Parse("Options\nV1 a 0 2\nR1 a 0 1k\n"+card+"\n.op\n.end\n",collectDeclarations:true);
        var result=RequestedAnalysis.Prepare(syntax);
        Assert.True(result.Success,string.Join(',',result.Diagnostics.Select(d=>d.Code)));
        Assert.Single(result.Operations);
    }
    [Theory]
    [InlineData(".options temp=1")]
    [InlineData(".options temp=27 temp=50")]
    [InlineData(".options temp=27 tnom=50")]
    [InlineData(".options temp={27}")]
    [InlineData(".options filetype=invalid")]
    public void Requested_analysis_refuses_options_outside_the_explicit_contract(string card)
    {
        var syntax=DeckParser.Parse("Options\nV1 a 0 2\nR1 a 0 1k\n"+card+"\n.op\n.end\n",collectDeclarations:true);
        var result=RequestedAnalysis.Prepare(syntax);
        Assert.False(result.Success);
        Assert.Empty(result.Operations);
    }
    [Theory]
    [InlineData(".temp 27.0",true)]
    [InlineData(".temp 27 50",false)]
    [InlineData(".temp 26",false)]
    [InlineData(".width in=72",true)]
    [InlineData(".width in=72 out=120",true)]
    [InlineData(".width in=-1",false)]
    [InlineData(".width in=72 IN=80",false)]
    [InlineData(".width in=72 unknown=3",false)]
    public void Remaining_temperature_and_width_cards_are_bounded(string card,bool expected)
    {
        var text="Directives\nV1 a 0 2\nR1 a 0 1k\n"+card+"\n.op\n.end\n";
        var syntax=DeckParser.Parse(text,collectDeclarations:true);
        var result=RequestedAnalysis.Prepare(syntax);
        Assert.Equal(expected,result.Success);
        if(result.Success)
        {
            var projected=Simulator.RequestedQualification.OriginalProjection.Create(text,syntax,Assert.Single(result.Operations),result);
            Assert.Equal(card.StartsWith(".temp",StringComparison.Ordinal),projected.Text.Contains(card,StringComparison.Ordinal));
        }
    }
    [FsCheck.Xunit.Property(MaxTest=120,Replay="2381,20261107")]
    public bool Unknown_or_nondefault_options_never_disappear(FsCheck.NonNegativeInt input)
    {
        var number=(input.Get%101)-50;
        var literal=number.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var prefix="Options\nV1 a 0 2\nR1 a 0 1k\n";
        var temp=DeckParser.Parse(prefix+".options acct temp="+literal+"\n.op\n.end\n",collectDeclarations:true);
        var unknown=DeckParser.Parse(prefix+".options acct temp=27 mystery="+literal+"\n.op\n.end\n",collectDeclarations:true);
        return RequestedAnalysis.Prepare(temp).Success==(number==27)
            && !RequestedAnalysis.Prepare(unknown).Success;
    }
}