using System.Text.Json;
using Simulator.Client;
using Simulator.Syntax;
using Xunit;
namespace Simulator.Frontend.Tests;

/// <summary>Numerical cards must change the dispatched integration policy, not merely pass parsing.</summary>
public sealed class RequestedTimeintTests
{
    [Theory]
    [InlineData(".options timeint reltol=1e-5 abstol=2u")]
    [InlineData(".options timeint RELTOL={tol} ABSTOL=2u")]
    public void RetainsEvaluatedRelativeAndCurrentBounds(string card)
    {
        var plan=Prepare(card);Assert.True(plan.Success,string.Join(',',plan.Diagnostics.Select(d=>d.Code)));
        var request=Assert.Single(Assert.Single(plan.Operations).Requests);
        using var json=JsonDocument.Parse(JsonSerializer.Serialize(request,WireFormat.Options));
        var options=json.RootElement.GetProperty("analysis").GetProperty("options");
        Assert.Equal(1e-5,options.GetProperty("relative_tolerance").GetDouble());
        Assert.Equal(2e-6,options.GetProperty("current_absolute_tolerance").GetDouble());
    }
    [Fact]
    public void MaximumStepCapsTheDeckAndFixedStepDisablesAdaptation()
    {
        var plan=Prepare(".options timeint delmax=0.2n conststep=1");
        Assert.True(plan.Success,string.Join(',',plan.Diagnostics.Select(d=>d.Code)));
        var options=Assert.IsType<TransientConfigured>(Assert.Single(Assert.Single(plan.Operations).Requests).Analysis).Options;
        Assert.InRange(Math.Abs(options.MaxStepS-2e-10),0,1e-25);Assert.Null(options.Adaptive);
    }
    [Theory]
    [InlineData("reltol=0")][InlineData("reltol=-1")][InlineData("reltol=missing")]
    [InlineData("reltol=.001 reltol=.002")][InlineData("reltol=.001 unknown=1")]
    [InlineData("conststep=2")][InlineData("delmax=0")][InlineData("method=gear")]
    public void RefusesInvalidOrUnknownWholeCards(string tail)=>Assert.False(Prepare(".options timeint "+tail).Success);
    [Fact]
    public void DuplicateCardsAndNonTransientScopesRemainAtomic()
    {
        Assert.False(Prepare(".options timeint reltol=.001\n.options timeint reltol=.002").Success);
        Assert.False(Prepare(".options timeint reltol=.001\n.op").Success);
    }
    [Theory]
    [InlineData(".options device debuglevel=-100")]
    [InlineData(".options output printheader=false printfooter=false")]
    public void ReportingNamespacesAreExplicitOmissions(string card)
    {
        var plan=Prepare(card);Assert.True(plan.Success,string.Join(',',plan.Diagnostics.Select(d=>d.Code)));
        Assert.Contains(plan.Observations,o=>o.Reason=="reporting_options_not_selected");
    }
    [Fact]
    public void DeviceTemperatureUsesTheExistingCelsiusEnvironment()
    {
        var plan=RequestedAnalysis.Prepare(DeckParser.Parse("Temperature\nV1 a 0 1\nR1 a 0 1k TC1=.001\n.options device temp=-55\n.dc V1 0 1 1\n.end\n",collectDeclarations:true));
        Assert.True(plan.Success,string.Join(',',plan.Diagnostics.Select(d=>d.Code)));Assert.Equal(-55,plan.OperatingTemperatureCelsius);
        var resistor=Assert.Single(plan.Operations[0].Requests[0].Circuit.Elements.OfType<Resistor>());
        Assert.InRange(Math.Abs(resistor.Ohms-918),0,1e-10);
    }
    [Fact]
    public void StatelessVoltageLawsCanRequestMemorylessTransientAnalyses()
    {
        var plan=RequestedAnalysis.Prepare(DeckParser.Parse("Behavior\nV1 a 0 DC 2 PWL(0 2 1m 3)\nR1 a 0 1k\nB1 b 0 V={ln(V(a))}\nR2 b 0 1k\n.tran 10u 1m\n.end\n",collectDeclarations:true));
        Assert.True(plan.Success,string.Join(',',plan.Diagnostics.Select(d=>d.Code)));
        Assert.Equal(new[]{"tran"},plan.Operations.Select(o=>o.Kind));
    }
    [Theory]
    [InlineData(".options device debuglevel=1")]
    [InlineData(".options output printfooter=false unknown=true")]
    [InlineData(".options device debuglevel=-100 temp=27")]
    [InlineData(".options device temp=-274")]
    [InlineData(".options device temp=-55\n.temp 27")]
    [InlineData(".options device temp=-55\n.options temp=27")]
    public void UnknownReportingAndConflictingTemperatureRemainRefused(string card)=>Assert.False(Prepare(card).Success);
    [FsCheck.Xunit.Property(MaxTest=64,Replay="1521,20261115")]
    public bool PositiveStepCapsNeverEnlargeTheAuthoredCeiling(int input)
    {
        var ps=100+(int)((uint)input%900);var plan=Prepare(".options timeint delmax="+ps+"p conststep=1");
        return plan.Success&&Math.Abs(((TransientConfigured)plan.Operations[0].Requests[0].Analysis).Options.MaxStepS-ps*1e-12)<=4e-25;
    }
    [Fact]
    public void DrivenSharpSwitchesHaveExplicitBoundedRetryControl()
    {
        var plan=RequestedAnalysis.Prepare(DeckParser.Parse("Switch retry\nV1 a 0 PULSE(0 3 0 10n 10n 10n 50n)\nV2 supply 0 5\nR1 b 0 1k\nS1 supply b a 0 sw\n.model sw vswitch(RON=1 ROFF=1meg VON=2.51 VOFF=2.5)\n.options timeint reltol=1e-3\n.tran .5n 200n\n.end\n",collectDeclarations:true));
        Assert.True(plan.Success,string.Join(',',plan.Diagnostics.Select(d=>d.Code)));
        var options=Assert.IsType<TransientConfigured>(plan.Operations[0].Requests[0].Analysis).Options;
        Assert.NotNull(options.Adaptive);Assert.True(options.Adaptive.MinStepS>0);
        Assert.True(options.Adaptive.MaxAttempts<=4*options.MaxSteps);
    }
    [Fact]
    public void NumericalExpressionsUseTheSelectedTemperature()
    {
        var plan=Prepare(".options device temp=-55\n.options timeint reltol={(temper+56)*1e-5}");
        Assert.True(plan.Success,string.Join(',',plan.Diagnostics.Select(d=>d.Code)));
        Assert.Equal(1e-5,Assert.IsType<TransientConfigured>(plan.Operations[0].Requests[0].Analysis).Options.RelativeTolerance);
    }
    [Fact]
    public void PassiveMediatedBehavioralControlsAreRefusedBeforeDispatch()
    {
        var plan=RequestedAnalysis.Prepare(DeckParser.Parse("Mediated control\nV1 in 0 2\nBexp ex 0 V={exp(V(in))}\nR1 ex mid 1k\nR2 mid 0 1k\nBlog log 0 V={ln(V(mid))}\nR3 log 0 1k\n.tran 10u 1m\n.end\n",collectDeclarations:true));
        Assert.False(plan.Success);Assert.Empty(plan.Operations);
        Assert.Contains(plan.Diagnostics,d=>d.Code=="unsupported_behavioral_transient_profile");
    }
    private static RequestedAnalysisPlan Prepare(string card)=>RequestedAnalysis.Prepare(DeckParser.Parse(
        "Time integration\n.param tol=1e-5\nV1 a 0 PULSE(0 1 0 1n 1n 10n 25n)\nR1 a b 1k\nC1 b 0 1n\n"+card+"\n.tran 1n 50n\n.end\n",collectDeclarations:true));
}
