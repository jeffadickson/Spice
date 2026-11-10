using Simulator.Syntax;
using Xunit;

namespace Simulator.Frontend.Tests;

public sealed class PoleZeroTests
{
    [Theory]
    [InlineData("pz", true)]
    [InlineData("pol", true)]
    [InlineData("zer", false)]
    public void GroundedRcHasOnePoleAndNoFiniteZero(string mode, bool pole)
    {
        var result = Analyze($"1 0 2 0 vol {mode}");
        Assert.True(result.Success, result.Diagnostic?.Code);
        Assert.Empty(result.Zeros);
        if (pole) Assert.Equal(-1e8, Assert.Single(result.Poles), 5);
        else Assert.Empty(result.Poles);
    }

    [Theory]
    [InlineData("1 0 2 0 cur pz", "unsupported_pz_directive")]
    [InlineData("1 2 2 0 vol pz", "unsupported_pz_ports")]
    [InlineData("2 0 1 0 vol pz", "unsupported_pz_topology")]
    public void UnsupportedContractsRefuseWithoutRoots(string directive, string code)
    {
        var result = Analyze(directive);
        Assert.Equal(code, result.Diagnostic?.Code);
        Assert.Empty(result.Poles);
    }

    private static PoleZeroResult Analyze(string directive) => PoleZeroSelection.Analyze(DeckParser.Parse(
        $"RC filter\nV1 1 0 0 AC 1\nR1 1 2 1k\nC1 2 0 10p\n.pz {directive}\n.print pz all\n.end\n",
        collectDeclarations: true));
}
