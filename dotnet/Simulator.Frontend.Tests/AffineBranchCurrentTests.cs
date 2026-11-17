using Simulator.Client;
using Simulator.Syntax;
using Xunit;
namespace Simulator.Frontend.Tests;

/// <summary>Affine branch-current laws retain scope, polarity and every controller.</summary>
public sealed class AffineBranchCurrentTests
{
    private static RequestedAnalysisPlan Prepare(string law,string quantity="I")=>RequestedAnalysis.Prepare(DeckParser.Parse(
        "Affine branch currents\nV1 a 0 1\nV2 b 0 2\nR1 a 0 1k\nR2 b 0 2k\nBcopy out 0 "+quantity+"={"+law+"}\nRload out 0 1k\n.op\n.end\n",collectDeclarations:true));

    /// <summary>Coefficients and DC offset are evaluated, not dropped after recognizing I().</summary>
    [Theory][InlineData("I")][InlineData("V")]
    public void ScaledSumRetainsBothControllersAndOffset(string quantity)
    {
        var plan=Prepare("2*I(V1)-3*I(V2)+.25",quantity);
        Assert.True(plan.Success,string.Join(',',plan.Diagnostics.Select(d=>d.Code)));
        var elements=plan.Operations[0].Requests[0].Circuit.Elements;
        if(quantity=="I"){
            Assert.Equal(new[]{-3d,2d},elements.OfType<Cccs>().Select(e=>e.Gain).Order().ToArray());
            Assert.Contains(elements,e=>e is Current{Amps:.25});
        }else{
            Assert.Equal(new[]{-3d,2d},elements.OfType<Ccvs>().Select(e=>e.Ohms).Order().ToArray());
            Assert.Contains(elements,e=>e is Voltage{Volts:.25});
        }
    }

    /// <summary>Invalid controllers remain invalid even when their coefficient cancels or is zero.</summary>
    [Theory][InlineData("I(Vmissing)-I(Vmissing)+I(V1)")][InlineData("0*I(R1)+I(V1)")]
    [InlineData("I(V1)*I(V2)")][InlineData("I(V1)+v(a)")][InlineData("I(V1)/I(V2)")]
    public void UnsupportedLawsRefuseAtomically(string law)
    {
        var plan=Prepare(law);Assert.False(plan.Success);Assert.Empty(plan.Operations);
    }

    /// <summary>An explicit source multiplier cannot be lost while expanding a branch-current alias.</summary>
    [Fact]
    public void CurrentAliasMultiplierIsNotSilentlyIgnored()
    {
        var syntax=DeckParser.Parse("Alias multiplier\nV1 in 0 1\nR1 in 0 1k\nG1 out 0 CUR='2*I(V1)' M=3\nR2 out 0 1k\n.op\n.end\n",collectDeclarations:true);
        var plan=RequestedAnalysis.Prepare(syntax);Assert.False(plan.Success);Assert.Empty(plan.Operations);
    }
}
