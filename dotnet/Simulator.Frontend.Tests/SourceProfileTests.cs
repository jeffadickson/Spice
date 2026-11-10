using Simulator.Client;
using Simulator.Syntax;
using Xunit;
namespace Simulator.Frontend.Tests;
public sealed class SourceProfileTests
{
    [Theory]
    [InlineData("dc")]
    [InlineData("pulse")]
    [InlineData("sin")]
    [InlineData("pwl")]
    [InlineData("exp")]
    public void Incomplete_reserved_clauses_are_not_reinterpreted_as_named_parameters(string keyword)
    {
        var syntax=DeckParser.Parse($"Reserved\n.param {keyword}=3\nV1 a 0 {keyword}\nR1 a 0 1k\n.end\n",collectDeclarations:true);
        Assert.False(OperatingPointSelection.Select(syntax).Success);
    }
    [Fact]
    public void AC_replacements_keep_phase_DC_bias_and_zero_unexcited_sources_separate()
    {
        var syntax=DeckParser.Parse("AC\nV1 a 0 DC 2 AC 1 -90\nI1 a 0 1m\nR1 a 0 1k\n.end\n",collectDeclarations:true);
        var result=OperatingPointSelection.Select(syntax).Prepared;
        Assert.True(result.Success);Assert.Equal(1,result.ExplicitAcSources);Assert.Equal(2,result.AcSources.Length);
        Assert.Equal(2,Assert.Single(result.Request!.Circuit.Elements.OfType<Voltage>()).Volts);
        Assert.InRange(Math.Abs(result.AcSources[0].Real),0,1e-14);Assert.Equal(-1,result.AcSources[0].Imag);
        Assert.Equal(0,result.AcSources[1].Real);Assert.Equal(0,result.AcSources[1].Imag);
    }
    [Theory]
    [InlineData("DC=2",2)]
    [InlineData("DC=2 AC=1",2)]
    [InlineData("DC 2 AC 1",2)]
    [InlineData("AC 1 DC -2",-2)]
    [InlineData("DC -2 AC 1 -90",-2)]
    [InlineData("AC 1 90",0)]
    public void Selected_dc_uses_only_the_dc_excitation(string header,double expected)
    {
        var syntax=DeckParser.Parse("Source\nV1 a 0 "+header+"\nR1 a 0 1k\n.end\n",collectDeclarations:true);
        var result=OperatingPointSelection.Select(syntax);
        Assert.True(result.Success,string.Join(',',result.Prepared.Diagnostics.Select(d=>d.Code)));
        Assert.Equal(expected,Assert.Single(result.Prepared.Request!.Circuit.Elements.OfType<Voltage>()).Volts);
    }
    [Theory]
    [InlineData("DC 1 AC 2 AC 3")]
    [InlineData("AC 1 DC 2 DC 3")]
    [InlineData("DC 1 AC missing")]
    [InlineData("DC 1 AC=")]
    [InlineData("DC 1 TRNOISE(0 1 1k 1 10k)")]
    public void Unsupported_or_invalid_source_profiles_are_atomic(string header)
    {
        var syntax=DeckParser.Parse("Source\nV1 a 0 "+header+"\nR1 a 0 1k\n.end\n",collectDeclarations:true);
        var result=OperatingPointSelection.Select(syntax);
        Assert.False(result.Success);Assert.Null(result.Prepared.Request);Assert.Empty(result.Prepared.Origins);
    }
}
