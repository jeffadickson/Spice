using FsCheck;
using FsCheck.Xunit;
using Simulator.Client;
using Simulator.Syntax;
using System.Globalization;
using System.Numerics;
using Xunit;
namespace Simulator.Frontend.Tests;

/// <summary>Fixed Celsius preparation remains at the ordinary requested boundary.</summary>
public sealed class BehavioralTemperatureTests
{
    [Theory]
    [InlineData("V",127,"tc1=.001",1.1)]
    [InlineData("I",127,"tc1=.001",1.1)]
    [InlineData("V",127,"tc1=.001 tc2=.00001",1.2)]
    [InlineData("I",27,"tc1=.001",1)]
    [InlineData("V",-73,"tc1=.01",0)]
    [InlineData("I",-173,"tc1=.01",-1)]
    public void Affine_relations_scale_all_terms_in_volts_or_amperes(string quantity,double temperature,string parameters,double factor)
    {
        var plan=Prepare($"B1 out 0 {quantity}=2*v(input)+3 {parameters}",temperature);
        Assert.True(plan.Success,string.Join(",",plan.Diagnostics.Select(d=>d.Code)));
        var elements=Assert.Single(Assert.Single(plan.Operations).Requests).Circuit.Elements;
        var coefficient=quantity=="V"?Assert.Single(elements.OfType<Vcvs>()).Gain:Assert.Single(elements.OfType<Vccs>()).Siemens;
        Assert.InRange(Math.Abs(coefficient-2*factor),0,1e-12);
        var offset=quantity=="V"?elements.OfType<Voltage>().Single(v=>v.Volts!=100).Volts:Assert.Single(elements.OfType<Current>()).Amps;
        Assert.InRange(Math.Abs(offset-3*factor),0,1e-12);
    }

    [Theory]
    [InlineData("B1 out 0 V=v(input) tc1=1e308",127)]
    [InlineData("B1 out 0 V=1e308*v(input) tc1=1",127)]
    [InlineData("B1 out 0 V=v(input) tc1=1 tc1=2",127)]
    [InlineData("B1 out 0 V=v(input) dtemp=1",127)]
    [InlineData("B1 out 0 V=v(input)*v(input) tc1=.001",127)]
    public void Unsupported_or_nonfinite_behavior_refuses_without_requests(string card,double temperature)
    {
        var plan=Prepare(card,temperature);Assert.False(plan.Success);Assert.Empty(plan.Operations);
    }

    [Fact]
    public void Literal_root_temperature_reaches_resistors_and_parameter_evaluation()
    {
        var plan=Prepare("R1 out 0 1k tc1=.001\nR2 input out {1000+temper}",127);
        Assert.True(plan.Success,string.Join(",",plan.Diagnostics.Select(d=>d.Code)));
        Assert.Equal(127,plan.OperatingTemperatureCelsius);
        var ohms=Assert.Single(Assert.Single(plan.Operations).Requests).Circuit.Elements.OfType<Resistor>().Select(r=>r.Ohms).ToArray();
        Assert.Equal(new[]{1100d,1127d},ohms);
    }

    [Theory]
    [InlineData(".temp 127\n.temp 27")]
    [InlineData(".temp -273.15")]
    [InlineData(".temp {127}")]
    [InlineData(".temp 127 128")]
    [InlineData(".temp 127\n.options temp=27")]
    [InlineData(".temp 127\n.csparam x=2")]
    public void Ambiguous_or_unqualified_root_temperature_refuses(string directives)
    {
        var syntax=DeckParser.Parse("Temperature\nV1 input 0 100\nR1 input 0 1k\n"+directives+"\n.op\n.end\n",collectDeclarations:true);
        var plan=RequestedAnalysis.Prepare(syntax);Assert.False(plan.Success);Assert.Empty(plan.Operations);
    }

    [Fact]
    public void Nondefault_semiconductor_temperature_is_not_silently_admitted()
    {
        var plan=Prepare("D1 input 0 dm\n.model dm d is=1f",127);
        Assert.False(plan.Success);Assert.Empty(plan.Operations);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void Conditional_early_failure_is_explicit_and_never_binds_later_plot(int fail)
    {
        var syntax=DeckParser.Parse($"Failure\nV1 a 0 1\nR1 a 0 1k\n.control\nop\nif {fail}\necho failed\nquit 1\nend\nop\necho later\nquit 0\n.endc\n.end\n",collectDeclarations:true);
        var plan=RequestedAnalysis.Prepare(syntax);Assert.True(plan.Success,string.Join(",",plan.Diagnostics.Select(d=>d.Code)));
        Assert.Equal(2,plan.Operations.Length);
        var plots=plan.Operations.ToDictionary(o=>o.ControlOrigin!.CommandRange.Start,_=>(IReadOnlyDictionary<string,Complex[]>)new Dictionary<string,Complex[]>());
        var result=ControlVectorExecution.ExecutePlots(syntax.Declarations!.ControlBlocks.Single().Commands,plots);
        Assert.Equal(fail,result.ExitCode);Assert.Equal(fail==1?"failed":"later",Assert.Single(result.Output));
    }

    [Theory]
    [InlineData("quit 1")]
    [InlineData("if 1\nquit 0\nend")]
    public void Unsupported_early_success_or_unconditional_exit_still_refuses(string body)
    {
        var syntax=DeckParser.Parse("Failure\nV1 a 0 1\n.control\nop\n"+body+"\nop\n.endc\n.end\n",collectDeclarations:true);
        Assert.False(RequestedAnalysis.Prepare(syntax).Success);
    }

    [Theory]
    [InlineData("B1 out 0 V=v(input) tc1=1e308")]
    [InlineData("B1 out 0 V=1e308*v(input) tc1=1")]
    [InlineData("B1 out 0 I=1e308+v(input) tc1=1")]
    [InlineData("Vother other 0 0\nB1 out 0 V=1e308*v(input)+v(other) tc1=1")]
    public void Nonfinite_scaled_law_preserves_actionable_failure_provenance(string card)
    {
        var plan=Prepare(card,127);Assert.False(plan.Success);Assert.Empty(plan.Operations);
        Assert.Contains(plan.Diagnostics,d=>d.Code=="invalid_behavior_temperature_factor");
    }

    [Fact]
    public void DC_temperature_sweep_overrides_the_literal_root_at_each_point()
    {
        var syntax=DeckParser.Parse("Sweep\nV1 input 0 100\nB1 out 0 V=v(input) tc1=.001\n.temp 127\n.dc temp 27 127 100\n.end\n",collectDeclarations:true);
        var plan=RequestedAnalysis.Prepare(syntax);Assert.True(plan.Success,string.Join(",",plan.Diagnostics.Select(d=>d.Code)));
        Assert.Equal(127,plan.OperatingTemperatureCelsius);
        var gains=Assert.Single(plan.Operations).Requests.Select(r=>Assert.Single(r.Circuit.Elements.OfType<Vcvs>()).Gain).ToArray();
        Assert.Equal(new[]{1d,1.1},gains);
    }
    [Property(MaxTest=128,Replay="5005,2026111301")]
    public bool Linear_behavior_temperature_has_an_independent_difference_identity(NonNegativeInt input)
    {
        var temperature=10+input.Get%191;
        var plan=Prepare("B1 out 0 V=2*v(input) tc1=.001",temperature);
        if(!plan.Success)return false;
        var gain=Assert.Single(Assert.Single(Assert.Single(plan.Operations).Requests).Circuit.Elements.OfType<Vcvs>()).Gain;
        return Math.Abs((gain-2)-.002*(temperature-27))<1e-12;
    }

    private static RequestedAnalysisPlan Prepare(string card,double temperature)
        =>RequestedAnalysis.Prepare(DeckParser.Parse("Temperature\nV1 input 0 100\n"+card+"\n.temp "+temperature.ToString("R",CultureInfo.InvariantCulture)+"\n.op\n.end\n",collectDeclarations:true));
}
