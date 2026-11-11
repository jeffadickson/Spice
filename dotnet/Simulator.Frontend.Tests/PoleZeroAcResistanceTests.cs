using Simulator.Syntax;
using Xunit;

namespace Simulator.Frontend.Tests;

public sealed class PoleZeroAcResistanceTests
{
    private static DeckSyntax Deck(string series="1k ac=4k",string shunt="4k",string mode="pz",string extra="") =>
        DeckParser.Parse($"Loaded RC\nVin input 0 DC 5 AC 3\nRseries input output {series}\nRload output 0 {shunt}\nCload output 0 1n\n{extra}.pz input 0 output 0 vol {mode}\n.end\n",collectDeclarations:true);

    [Theory]
    [InlineData("pz",true)]
    [InlineData("pol",true)]
    [InlineData("zer",false)]
    public void Loaded_rc_uses_ac_conductance_and_requested_root_mode(string mode,bool hasPole)
    {
        var result=PoleZeroSelection.Analyze(Deck(mode:mode));
        Assert.True(result.Success,result.Diagnostic?.Code);
        Assert.NotNull(result.CircuitPreparation);
        Assert.Equal(1000,Assert.Single(result.CircuitPreparation.Request!.Circuit.Elements
            .OfType<Simulator.Client.Resistor>(),r=>r.Ohms==1000).Ohms);
        Assert.Empty(result.Zeros);
        if(hasPole)Assert.Equal(-500000,Assert.Single(result.Poles),6);
        else Assert.Empty(result.Poles);
    }

    [Fact]
    public void Pz_preparation_does_not_broaden_the_legacy_selected_profile()
    {
        var syntax=Deck();
        Assert.False(OperatingPointSelection.Select(syntax).Prepared.Success);
        Assert.True(PoleZeroSelection.Analyze(syntax).Success);
    }

    [Theory]
    [InlineData("Rextra input output 10k\n")]
    [InlineData("Cextra input 0 1n\n")]
    [InlineData("Gextra output 0 input 0 1m\n")]
    public void Unmodeled_loading_is_refused_without_partial_roots(string extra)
    {
        var result=PoleZeroSelection.Analyze(Deck(extra:extra));
        Assert.False(result.Success);Assert.Empty(result.Poles);Assert.Empty(result.Zeros);
    }

    [Fact]
    public void Unloaded_rc_does_not_ignore_an_additional_controlled_source()
    {
        var syntax=DeckParser.Parse("Extra device\nVin input 0 DC 5 AC 3\nRseries input output 1k\nCload output 0 1n\nGextra output 0 input 0 1m\n.pz input 0 output 0 vol pz\n.end\n",collectDeclarations:true);
        var result=PoleZeroSelection.Analyze(syntax);
        Assert.False(result.Success);Assert.Empty(result.Poles);Assert.Empty(result.Zeros);
    }

    [FsCheck.Xunit.Property(MaxTest=64,Replay="5001,20261111")]
    public void Loaded_rc_pole_is_the_sum_of_positive_branch_rates(ushort a,ushort b)
    {
        var r1=1+(int)a;var r2=1+(int)b;
        var result=PoleZeroSelection.Analyze(Deck(series:$"{r1}",shunt:$"{r2}"));
        Assert.True(result.Success,result.Diagnostic?.Code);
        // Independent parallel-resistance construction, rather than reusing stamping logic.
        var parallel=(double)r1*r2/(r1+r2);
        var expected=-1/(parallel*1e-9);
        Assert.InRange(Math.Abs(Assert.Single(result.Poles)-expected)/Math.Abs(expected),0,1e-12);
    }
}
