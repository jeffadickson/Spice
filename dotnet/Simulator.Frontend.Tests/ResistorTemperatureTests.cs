using FsCheck;
using FsCheck.Xunit;
using Simulator.Client;
using Simulator.Syntax;
using System.Globalization;
using Xunit;

namespace Simulator.Frontend.Tests;

/// <summary>Temperature is resolved through the ordinary requested-analysis compiler.</summary>
public sealed class ResistorTemperatureTests
{
    [Theory]
    [InlineData("R1 a 0 1k tc1=2m tc2=30u","",10,974.67)]
    [InlineData("R1 a 0 1k tc1=2m tc2=30u","",100,1305.87)]
    [InlineData("R1 a 0 rt",".model rt r r=1000 tc1=2m tc2=30u",100,1305.87)]
    [InlineData("R1 a 0 rt tc1=0",".model rt r r=1000 tc1=2m tc2=30u",100,1159.87)]
    [InlineData("R1 a 0 rt tc1=0 tc2=0",".model rt r r=1000 tc1=2m tc2=30u",100,1000)]
    [InlineData("R1 a 0 rt",".model rt r r=1000 tnom=100 tc1=2m",100,1000)]
    [InlineData("R1 a 0 1k tc1=2m m=2 scale=3 ac=2k","",100,1719)]
    public void Polynomial_model_instance_and_nominal_parameters_preserve_units(string card,string model,double temperature,double expected)
    {
        var plan=Prepare(card,model,temperature);
        Assert.True(plan.Success,string.Join(",",plan.Diagnostics.Select(d=>d.Code)));
        var request=Assert.Single(Assert.Single(plan.Operations).Requests);
        Assert.InRange(Assert.Single(request.Circuit.Elements.OfType<Resistor>()).Ohms,expected-1e-9,expected+1e-9);
    }

    [Theory]
    [InlineData("R1 a 0 rt",".model rt r r=1000 tc1=1 tc2=1 tce=0")]
    [InlineData("R1 a 0 rt tce=0",".model rt r r=1000 tc1=1 tc2=1 tce=700m")]
    [InlineData("R1 a 0 1k tc1=1 tc2=1 tce=0","")]
    public void Explicit_zero_TCE_overrides_nonzero_polynomial_and_model_TCE(string card,string model)
    {
        var plan=Prepare(card,model,100);
        Assert.True(plan.Success,string.Join(",",plan.Diagnostics.Select(d=>d.Code)));
        Assert.Equal(1000,Assert.Single(Assert.Single(Assert.Single(plan.Operations).Requests).Circuit.Elements.OfType<Resistor>()).Ohms);
    }

    [Theory]
    [InlineData("R1 a 0 1k tc1=1", "",10)]
    [InlineData("R1 a 0 1k tce=1e300", "",100)]
    [InlineData("R1 a 0 1k tce=-1e300", "",100)]
    [InlineData("R1 a 0 1k tc1=1 tc1=2", "",100)]
    [InlineData("R1 a 0 rt", ".model rt r r=1000 tnom=-300",100)]
    [InlineData("R1 a 0 1k temp=20 tc1=1", "",100)]
    [InlineData("R1 a 0 1k tc3=1", "",100)]
    public void Invalid_unsupported_or_nonpositive_temperature_profiles_refuse_atomically(string card,string model,double temperature)
    {
        var plan=Prepare(card,model,temperature);
        Assert.False(plan.Success);Assert.Empty(plan.Operations);
    }

    [Theory]
    [InlineData("tc1=1",10)]
    [InlineData("tce=1e300",100)]
    [InlineData("tce=-1e300",100)]
    public void Invalid_factors_preserve_temperature_failure_provenance(string parameters,double temperature)
    {
        var plan=Prepare("R1 a 0 1k "+parameters,"",temperature);
        Assert.False(plan.Success);Assert.Empty(plan.Operations);
        // Callers must distinguish a failed temperature law from invalid nominal R.
        Assert.Contains(plan.Diagnostics,d=>d.Code=="invalid_resistor_temperature_factor");
    }
    [Fact]
    public void Temperature_factor_applies_to_explicit_AC_resistance_and_preserves_DC_preparation()
    {
        var syntax=DeckParser.Parse("AC temperature\nV1 a 0 5 AC 1\nR1 a 0 rt ac=2k m=2 scale=3\n.model rt r r=1k tnom=0 tc1=.01\n.ac lin 1 100 100\n.end\n",collectDeclarations:true);
        var plan=RequestedAnalysis.Prepare(syntax);
        Assert.True(plan.Success,string.Join(",",plan.Diagnostics.Select(d=>d.Code)));
        var dc=Assert.Single(plan.CircuitPreparation!.Request!.Circuit.Elements.OfType<Resistor>());
        var ac=Assert.Single(Assert.Single(Assert.Single(plan.Operations).Requests).Circuit.Elements.OfType<Resistor>());
        Assert.Equal(1905,dc.Ohms,9);Assert.Equal(3810,ac.Ohms,9);
    }
    [Property(MaxTest=128,Replay="5004,2026111301")]
    public bool Linear_temperature_law_obeys_an_independent_difference_identity(NonNegativeInt input)
    {
        var temperature=10+input.Get%91;
        var plan=Prepare("R1 a 0 1k tc1=2m","",temperature);
        if(!plan.Success)return false;
        var resistance=Assert.Single(Assert.Single(Assert.Single(plan.Operations).Requests).Circuit.Elements.OfType<Resistor>()).Ohms;
        return Math.Abs((resistance-1000)-2*(temperature-27))<1e-10;
    }

    private static RequestedAnalysisPlan Prepare(string card,string model,double temperature)
    {
        var text=temperature.ToString("R",CultureInfo.InvariantCulture);
        return RequestedAnalysis.Prepare(DeckParser.Parse("Temperature\nV1 a 0 5\n"+card+"\n"+model+"\n.dc temp "+text+" "+text+" 1\n.end\n",collectDeclarations:true));
    }
}
