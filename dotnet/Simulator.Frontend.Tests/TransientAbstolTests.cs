using Simulator.Client;
using Simulator.Syntax;
using Xunit;

namespace Simulator.Frontend.Tests;

public sealed class TransientAbstolTests
{
    [Fact]
    public void CarriesAnEvaluatedAmpereToleranceIntoTheTransientWireRequest()
    {
        var plan = Prepare(".param amps=1u\n.opt list abstol={amps} method=gear maxord=1");
        Assert.True(plan.Success, string.Join(",", plan.Diagnostics.Select(d => d.Code)));
        var request = Assert.Single(Assert.Single(plan.Operations).Requests);
        var analysis = Assert.IsType<TransientConfigured>(request.Analysis);
        Assert.Equal(1e-6, analysis.Options.CurrentAbsoluteTolerance);
        Assert.Equal("backward_euler", analysis.Options.Method);
        using var wire = System.Text.Json.JsonDocument.Parse(System.Text.Json.JsonSerializer.Serialize(request, WireFormat.Options));
        Assert.Equal(1e-6, wire.RootElement.GetProperty("analysis").GetProperty("options").GetProperty("current_absolute_tolerance").GetDouble());
    }

    [Theory]
    [InlineData(".opt abstol=0")]
    [InlineData(".opt abstol=-1u")]
    [InlineData(".opt abstol=missing")]
    [InlineData(".opt abstol=1u abstol=2u")]
    [InlineData(".opt abstol=1u\n.options abstol=2u")]
    [InlineData(".opt abstol=1u reltol=.001")]
    [InlineData(".opt abstol=1u\n.op")]
    public void RefusesInvalidAmbiguousOrUnimplementedOptionScopes(string cards)
    {
        Assert.Empty(Prepare(cards).Operations);
    }

    private static RequestedAnalysisPlan Prepare(string cards) =>
        RequestedAnalysis.Prepare(DeckParser.Parse(
            $"Transient tolerance\nV1 a 0 1\nR1 a b 1k\nC1 b 0 1n\n{cards}\n.tran 1n 100n\n.end\n",
            collectDeclarations: true));
}
