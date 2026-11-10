using Simulator.Client;
using Simulator.Syntax;
using Xunit;

namespace Simulator.Frontend.Tests;

public sealed class SensitivityTests
{
    [BjtOracleTheory]
    [InlineData("lu")]
    [InlineData("faer")]
    public async Task DifferentialDividerSensitivityHasCorrectSignsAndUnits(string backend)
    {
        var deck = DeckParser.Parse(
            "Divider sensitivity\nVin in 0 2\nR1 in out 1k\nR2 out 0 2k\n.sens v(out,in)\n.end\n",
            collectDeclarations: true);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var result = await SensitivitySelection.ExecuteAsync(deck,
            new SimulationClient(Environment.GetEnvironmentVariable("SPICE_SEMICONDUCTOR_ENGINE")!), backend, deadline.Token);
        Assert.True(result.Success, result.Diagnostic?.Code);
        Assert.Equal(-4d / 9e3, result.Derivatives.Single(d => d.Parameter == "R1").Value, 10);
        Assert.Equal(2d / 9e3, result.Derivatives.Single(d => d.Parameter == "R2").Value, 10);
        Assert.Equal(-1d / 3, result.Derivatives.Single(d => d.Parameter == "Vin").Value, 8);
    }

    [Fact]
    public async Task CurrentObservableRefusesBeforeTransport()
    {
        var deck = DeckParser.Parse("Unsupported sensitivity\nV1 in 0 2\nR1 in 0 1k\n.sens i(V1)\n.end\n",
            collectDeclarations: true);
        var result = await SensitivitySelection.ExecuteAsync(deck, new SimulationClient("unused"));
        Assert.Equal("unsupported_sens_directive", result.Diagnostic?.Code);
        Assert.Empty(result.Derivatives);
    }
}
