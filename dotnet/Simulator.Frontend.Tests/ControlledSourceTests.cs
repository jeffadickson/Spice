using Simulator.Syntax;
using Xunit;
namespace Simulator.Frontend.Tests;
public sealed class ControlledSourceTests
{
    [Theory]
    [InlineData("E1 out 0 in 0 3")]
    [InlineData("G1 out 0 in 0 -2m")]
    [InlineData("E1 out 0 (in 0) {gain}")]
    public void Linear_voltage_controlled_sources_prepare_through_the_generated_frontend(string card)
    {
        var syntax=DeckParser.Parse("Controlled\n.param gain=3\nV1 in 0 DC 2 AC 1\n"+card+"\nR1 out 0 1k\n.end\n",collectDeclarations:true);
        Assert.False(OperatingPointBridge.Lower(syntax).Success);
        var result=OperatingPointSelection.Select(syntax);
        Assert.True(result.Success,string.Join(',',result.Prepared.Diagnostics.Select(d=>d.Code)));
        Assert.Equal(3,result.Prepared.Request!.Circuit.Elements.Length);
    }
    [Theory]
    [InlineData("E1 out 0 POLY(1) in 0 1 2")]
    [InlineData("G1 out 0 value={v(in)}")]
    [InlineData("E1 out 0 TABLE {v(in)} (0,0) (1,1)")]
    public void Nonlinear_control_profiles_remain_atomic_refusals(string card)
    {
        var syntax=DeckParser.Parse("Controlled\nV1 in 0 2\n"+card+"\nR1 out 0 1k\n.end\n",collectDeclarations:true);
        var result=OperatingPointSelection.Select(syntax);
        Assert.False(result.Success);Assert.Null(result.Prepared.Request);Assert.Empty(result.Prepared.Origins);
    }
}