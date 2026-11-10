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

    [Fact]
    public void EqualBridgeTHasTwoDistinctPolesAndARepeatedZero()
    {
        var result = PoleZeroSelection.Analyze(DeckParser.Parse(
            "Bridge-T\nV1 1 0 12 AC 1\nC1 1 2 1u\nC2 2 3 1u\nR3 2 0 1k\nR4 1 3 1k\n.op\n.pz 1 0 3 0 vol pz\n.end\n",
            collectDeclarations: true));
        Assert.True(result.Success, result.Diagnostic?.Code);
        Assert.Collection(result.Poles,
            pole => Assert.Equal(-2618.03398875, pole, 6),
            pole => Assert.Equal(-381.96601125, pole, 6));
        Assert.Collection(result.Zeros,
            zero => Assert.Equal(-1000, zero, 6),
            zero => Assert.Equal(-1000, zero, 6));
    }

    [Theory]
    [InlineData("R4 2 0 1k", "unsupported_pz_topology")]
    [InlineData("R4 1 3 10k", "unsupported_pz_root_domain")]
    public void BridgeTRefusesDuplicateBranchesAndComplexZeros(string bridge, string code)
    {
        var result = PoleZeroSelection.Analyze(DeckParser.Parse(
            $"Bridge-T\nV1 1 0 12 AC 1\nC1 1 2 1u\nC2 2 3 1u\nR3 2 0 1k\n{bridge}\n.pz 1 0 3 0 vol pz\n.end\n",
            collectDeclarations: true));
        Assert.Equal(code, result.Diagnostic?.Code);
        Assert.Empty(result.Poles);
        Assert.Empty(result.Zeros);
    }

    private static PoleZeroResult Analyze(string directive) => PoleZeroSelection.Analyze(DeckParser.Parse(
        $"RC filter\nV1 1 0 0 AC 1\nR1 1 2 1k\nC1 2 0 10p\n.pz {directive}\n.print pz all\n.end\n",
        collectDeclarations: true));
}
