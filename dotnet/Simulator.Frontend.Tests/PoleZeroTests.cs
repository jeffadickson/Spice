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
    [InlineData("1 0 2 0 cur pz", "unsupported_pz_topology")]
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

    [Fact]
    public void BufferedRcCascadeProducesAllThreePoles()
    {
        var result = PoleZeroSelection.Analyze(DeckParser.Parse(
            "Cascade\nV1 1 0 0 AC 1\nR1 1 2 1k\nC1 2 0 10p\nE2 3 0 2 0 10\nR2 3 4 1k\nC2 4 0 1.25p\nE3 5 0 4 0 10\nR3 5 6 1k\nC3 6 0 .02p\n.pz 1 0 6 0 vol pz\n.end\n",
            collectDeclarations: true));
        Assert.True(result.Success, result.Diagnostic?.Code);
        Assert.Equal(new[] { -5e10, -8e8, -1e8 }, result.Poles.Order().Select(p => Math.Round(p, 3)));
        Assert.Empty(result.Zeros);
    }

    [Theory]
    [InlineData("", true)]
    [InlineData("Rload 1 2 1k\n", false)]
    [InlineData("Gfeedback 1 0 3 0 1\n", false)]
    public void CurrentDrivenRlCascadePreservesMultiplicityAndRejectsLoading(string extra, bool success)
    {
        var deck = $"Cascade\nIin 1 0 AC\nR1 1 0 1\nL1 1 0 .05\nG2 2 0 1 0 1\nR2 2 0 1\nL2 2 0 .05\nG3 3 0 2 0 1\nR3 3 0 1\nL3 3 0 .05\n{extra}.pz 1 0 3 0 cur pol\n.end\n";
        var result = PoleZeroSelection.Analyze(DeckParser.Parse(deck, collectDeclarations: true));
        Assert.Equal(success, result.Success);
        if (success) Assert.Equal(new[] { -20d, -20d, -20d }, result.Poles.ToArray());
        else Assert.Empty(result.Poles);
    }

    [Theory]
    [InlineData("AC", 0)]
    [InlineData("AC DC 2", 2)]
    [InlineData("DC 2 AC", 2)]
    public void BareAcUsesUnitMagnitudeAndPreservesExplicitDc(string clauses, double dc)
    {
        var deck = DeckParser.Parse($"Source\n.param ac=3\nIin in 0 {clauses}\nR1 in 0 1k\n.end\n", collectDeclarations: true);
        var circuit = OperatingPointSelection.Select(deck).Prepared;
        Assert.True(circuit.Success, string.Join(",", circuit.Diagnostics.Select(d => d.Code)));
        var source = Assert.Single(circuit.Request!.Circuit.Elements.OfType<Simulator.Client.Current>());
        Assert.Equal(dc, source.Amps);
        var phasor = Assert.Single(circuit.AcSources);
        Assert.Equal(source.Id, phasor.Id);
        Assert.Equal(1, phasor.Real);
        Assert.Equal(0, phasor.Imag);
    }

    [Theory]
    [InlineData("pz", true, true)]
    [InlineData("pol", true, false)]
    [InlineData("zer", false, true)]
    public void CurrentDrivenHighPassUsesBothGroundedResistances(string mode, bool pole, bool zero)
    {
        var syntax = DeckParser.Parse(
            $"High pass\nR1 1 0 1k\nR2 2 0 3k\nC1 1 2 1n\n.pz 1 0 2 0 cur {mode}\n.end\n",
            collectDeclarations: true);
        var result = PoleZeroSelection.Analyze(syntax);
        Assert.True(result.Success, result.Diagnostic?.Code);
        if (pole) Assert.Equal(-250000, Assert.Single(result.Poles), 6);
        else Assert.Empty(result.Poles);
        if (zero) Assert.Equal(0, Assert.Single(result.Zeros));
        else Assert.Empty(result.Zeros);
    }

    private static PoleZeroResult Analyze(string directive) => PoleZeroSelection.Analyze(DeckParser.Parse(
        $"RC filter\nV1 1 0 0 AC 1\nR1 1 2 1k\nC1 2 0 10p\n.pz {directive}\n.print pz all\n.end\n",
        collectDeclarations: true));
}
