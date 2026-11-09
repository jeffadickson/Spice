using Simulator.Client;
using Simulator.Syntax;
using Xunit;

namespace Simulator.Frontend.Tests;

public sealed class JfetTests
{
    [Theory]
    [InlineData("NJF", false, -3.5)]
    [InlineData("PJF", true, 3.5)]
    public void SquareLawReusesIntrinsicTerminalsAndGateJunctions(string family, bool pChannel, double threshold)
    {
        var prepared = Prepare(family, "LEVEL=1 VTO=-3.5 BETA=410u RD=200");
        Assert.True(prepared.Success, string.Join(",", prepared.Diagnostics.Select(d => d.Code)));
        var elements = prepared.Request!.Circuit.Elements;
        var channel = Assert.Single(elements.OfType<Mos1Channel>());
        Assert.Equal(pChannel, channel.Model.PChannel);
        Assert.Equal(threshold, channel.Model.ThresholdVolts);
        Assert.Equal(820e-6, channel.Model.BetaAmpsPerVoltSquared, 14);
        Assert.Equal(channel.Gate, channel.Bulk);
        Assert.Contains(elements.OfType<Resistor>(), r => r.Ohms == 200 && r.N == channel.P);
        Assert.Equal(2, elements.OfType<SpiceDiode>().Count(d => (pChannel ? d.N : d.P) == channel.Gate));
    }

    [Theory]
    [InlineData("LEVEL=2")]
    [InlineData("BETA=0")]
    [InlineData("RD=-1")]
    [InlineData("CGS=1p")]
    public void OutsideProfileRefusesWithoutPublishing(string model)
    {
        var prepared = Prepare("NJF", model);
        Assert.False(prepared.Success);
        Assert.Null(prepared.Request);
    }

    private static BridgeResult Prepare(string family, string model) =>
        OperatingPointSelection.Select(DeckParser.Parse(
            $"JFET fixture\nVD d 0 5\nVG g 0 -2\nJ1 d g 0 q\n.model q {family}({model})\n.op\n.end\n",
            collectDeclarations: true)).Prepared;
}
