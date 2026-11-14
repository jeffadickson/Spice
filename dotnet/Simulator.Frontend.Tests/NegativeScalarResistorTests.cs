using System.Globalization;
using Simulator.Client;
using Simulator.Syntax;
using Xunit;

namespace Simulator.Frontend.Tests;

/// <summary>A finite negative scalar resistance is an active conductance.</summary>
public sealed class NegativeScalarResistorTests
{
    private static DeckSyntax Deck(string resistance,string extra="")=>DeckParser.Parse(
        $"Signed resistance\nV1 a 0 DC 5 AC 1\nR1 a 0 {resistance}\n{extra}\n.op\n.end\n",collectDeclarations:true);

    [Theory]
    [InlineData("-100", "", -.01)]
    [InlineData("{r}", ".param r=-250", -.004)]
    public void Requested_scalar_negative_resistance_lowers_to_exact_self_controlled_conductance(string resistance,string extra,double expected)
    {
        var plan=RequestedAnalysis.Prepare(Deck(resistance,extra));
        Assert.True(plan.Success,string.Join(',',plan.Diagnostics.Select(d=>d.Code)));
        var conductance=Assert.Single(plan.CircuitPreparation!.Request!.Circuit.Elements.OfType<Vccs>());
        Assert.Equal(conductance.P,conductance.Cp);
        Assert.Equal(conductance.N,conductance.Cn);
        Assert.Equal(expected,conductance.Siemens);
        Assert.Empty(plan.CircuitPreparation.AcResistances);
        Assert.Contains(plan.CircuitPreparation.Origins,o=>o.WireId==conductance.Id&&o.LocalName=="R1");
    }

    [Theory]
    [InlineData("0", "")]
    [InlineData("-1e-320", "")]
    [InlineData("RM", ".model RM R R=-100")]
    public void Zero_nonfinite_conductance_and_unqualified_negative_model_refuse_atomically(string resistance,string extra)
    {
        var plan=RequestedAnalysis.Prepare(Deck(resistance,extra));
        Assert.False(plan.Success);
        Assert.Empty(plan.Operations);
    }

    [Fact]
    public void Positive_resistance_and_legacy_selected_OP_admission_remain_explicit()
    {
        var positive=RequestedAnalysis.Prepare(Deck("100"));
        Assert.Single(positive.CircuitPreparation!.Request!.Circuit.Elements.OfType<Resistor>());
        Assert.False(OperatingPointSelection.Select(Deck("-100")).Success);
    }

    [FsCheck.Xunit.Property(MaxTest=128,Replay="1001,2026111403")]
    public bool Negative_scalar_retains_the_signed_reciprocal(FsCheck.NonNegativeInt value)
    {
        var resistance=-(1d+value.Get);
        var plan=RequestedAnalysis.Prepare(Deck(resistance.ToString("R",CultureInfo.InvariantCulture)));
        if(!plan.Success)return false;
        var conductance=plan.CircuitPreparation!.Request!.Circuit.Elements.OfType<Vccs>().Single();
        // One reciprocal and multiplication may incur two binary64 roundings.
        return conductance.Siemens<0&&Math.Abs(conductance.Siemens*resistance-1)<=4e-16;
    }
}
