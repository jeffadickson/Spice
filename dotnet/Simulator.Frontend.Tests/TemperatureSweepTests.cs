using Simulator.Client;
using Simulator.Syntax;
using Xunit;

namespace Simulator.Frontend.Tests;

public sealed class TemperatureSweepTests
{
    [Fact]
    public void RebuildsResistorsAtEveryCelsiusPoint()
    {
        var plan = Prepare("R1 a 0 rt", "dc temp 10 30 10");
        Assert.True(plan.Success, string.Join(",", plan.Diagnostics.Select(d => d.Code)));
        var operation = Assert.Single(plan.Operations);
        Assert.Equal("degC", operation.CoordinateUnit);
        Assert.Equal(new[] { 10d, 20d, 30d }, operation.Coordinates.ToArray());
        Assert.Equal(new[] { 1010d, 1020d, 1030d },
            operation.Requests.Select(r => Assert.Single(r.Circuit.Elements.OfType<Resistor>()).Ohms));
    }

    [Fact]
    public void DiodeSweepReevaluatesIsAndScalesToTheRequestedTemperature()
    {
        var syntax = DeckParser.Parse(
            "Temperature\nV1 a 0 .4\nD1 a 0 dm\n.model dm d IS='1e-12*temper'\n.dc temp 10 30 10\n.end\n",
            collectDeclarations: true);
        var plan = RequestedAnalysis.Prepare(syntax);
        Assert.True(plan.Success, string.Join(",", plan.Diagnostics.Select(d => d.Code)));
        var models = Assert.Single(plan.Operations).Requests
            .Select(r => Assert.Single(r.Circuit.Elements.OfType<SpiceDiode>()).Model).ToArray();
        var expected = new[] { 6.383202139607846e-13, 6.6871216169522605e-12, 4.726296409426534e-11 };
        Assert.Equal(expected.Length, models.Length);
        for (var i = 0; i < expected.Length; i++)
            Assert.InRange(models[i].SaturationAmps / expected[i], 1 - 1e-12, 1 + 1e-12);
        Assert.All(models, m => Assert.InRange(m.ThermalVolts, .024, .027));
        Assert.True(models[0].ThermalVolts < models[^1].ThermalVolts);
    }

    [Theory]
    [InlineData("D1 a 0 dm", "dc temp 10 30 10")]
    [InlineData("R1 a 0 rt", "dc temp -300 -280 10")]
    [InlineData("R1 a 0 rt", "dc temp 10 30 10 V1 1 2 1")]
    public void UnqualifiedTemperaturePhysicsAndGridsRefuse(string element, string sweep)
    {
        Assert.Empty(Prepare(element, sweep).Operations);
    }

    private static RequestedAnalysisPlan Prepare(string element, string sweep) =>
        RequestedAnalysis.Prepare(DeckParser.Parse(
            $"Temperature\nV1 a 0 5\n{element}\n.model rt r r='1000+temper'\n.model dm d CJO=2p\n.control\nlet success_count = 0\n{sweep}\nlet gold = \"temp-sweep\"\nlet err = vecmax(abs((-v(a)/i(v1)-1000)/gold-1))\nif err > 1e-12\nquit 1\nelse\nquit 0\nend\n.endc\n.end\n",
            collectDeclarations: true));
}
