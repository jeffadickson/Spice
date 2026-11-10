using Simulator.Client;
using Simulator.Syntax;
using Xunit;

namespace Simulator.Frontend.Tests;

public sealed class ZeroFrequencyAcTests
{
    [Fact]
    public void OriginalStyleControlRetainsZeroHertzAndUnspacedDivision()
    {
        var syntax = DeckParser.Parse(
            "Zero frequency\nV1 in 0 DC=0 AC=1\nR1 in out 1k\nR2 out 0 1k\n.control\nac lin 1 0Hz 0Hz\nlet gold = .5\nlet err = abs(v(out)/gold - 1)\nif err > 1e-15\nquit 1\nelse\nquit 0\nend\n.endc\n.end\n",
            collectDeclarations: true);
        var plan = RequestedAnalysis.Prepare(syntax);
        Assert.True(plan.Success, string.Join(",", plan.Diagnostics.Select(d => d.Code)));
        var operation = Assert.Single(plan.Operations);
        Assert.Equal(0, Assert.Single(Assert.IsType<Ac>(Assert.Single(operation.Requests).Analysis).FrequenciesHz));
    }

    [Fact]
    public void ReportingExecutesRealZeroHertzAndRefusesComplexProjection()
    {
        var syntax = DeckParser.Parse(
            "Zero frequency reporting\nV1 in 0 AC 1\nR1 in out 1k\nR2 out 0 1k\n.control\nac lin 1 0 0\nlet gold = .5\nlet err = abs(v(out)/gold - 1)\nif err > 1e-15\nquit 1\nelse\nquit 0\nend\n.endc\n.end\n",
            collectDeclarations: true);
        var plan = RequestedAnalysis.Prepare(syntax);
        Assert.True(plan.Success, string.Join(",", plan.Diagnostics.Select(d => d.Code)));
        var wire = plan.CircuitPreparation!.RootNodes["out"];
        var result = RequestedAnalysis.ExecuteTerminalReporting(syntax, plan, [Response(0)]);
        Assert.Equal(0, result.ExitCode);
        Assert.Equal(0, Assert.Single(result.Locals["err"]));
        var error = Assert.Throws<InvalidDataException>(() =>
            RequestedAnalysis.ExecuteTerminalReporting(syntax, plan, [Response(.001)]));
        Assert.Equal("control_reporting_requires_real_zero_frequency_sample", error.Message);

        SimulationResponse Response(double imaginary)
        {
            var request = Assert.Single(Assert.Single(plan.Operations).Requests);
            var manifest = new ResultManifest("1", request.CaseId, "succeeded", true,
                [new($"v({wire})", "voltage", "V")], [], [new(0, [new(.5, imaginary)])],
                [], [], null, new("lu", "cpu", 128, false, false, true, false, "test"), "synthetic");
            var payload = System.Text.Json.JsonSerializer.SerializeToElement(manifest,
                new System.Text.Json.JsonSerializerOptions { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.SnakeCaseLower });
            return new("1", "succeeded", true, payload);
        }
    }

    [Theory]
    [InlineData("lin 1 -1 0")]
    [InlineData("dec 2 0 10")]
    [InlineData("oct 2 0 10")]
    public void InvalidFrequencyGridsStillRefuse(string grid)
    {
        var syntax = DeckParser.Parse($"Invalid AC\nV1 a 0 AC 1\nR1 a 0 1k\n.ac {grid}\n.end\n", collectDeclarations: true);
        Assert.False(RequestedAnalysis.Prepare(syntax).Success);
    }
}
