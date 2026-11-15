using Simulator.Client;
using Simulator.Frontend;
using Simulator.Syntax;
using Xunit;
namespace Simulator.Frontend.Tests;
/// <summary>Quoted behavioral laws retain voltage dependence and lexical multiplicity.</summary>
public sealed class QuotedBehaviorTests
{
    [Fact]
    public void Randomized_quoted_current_scales_gain_and_offset_together()
    {
        var config=FsCheck.Config.QuickThrowOnFailure.WithMaxTest(128).WithReplay(Microsoft.FSharp.Core.FSharpOption<FsCheck.Replay>.Some(new(new FsCheck.Rnd(2026111505UL),null)));
        FsCheck.Check.One(config,FsCheck.Fluent.Prop.ForAll<FsCheck.NonNegativeInt>(size=>{
            var m=(1+size.Get%100)*.25;
            var plan=RequestedAnalysis.Prepare(DeckParser.Parse(FormattableString.Invariant($"property\nV1 in 0 1\nR1 out 0 100\nB1 out 0 I='2m*v(in)+3m' M={m}\n.op\n.end\n"),collectDeclarations:true));
            if(!plan.Success)return false;
            var elements=plan.Operations[0].Requests[0].Circuit.Elements;
            return Math.Abs(elements.OfType<Vccs>().Single().Siemens-.002*m)<1e-12&&elements.OfType<Current>().Any(c=>Math.Abs(c.Amps-.003*m)<1e-12);
        }));
    }
    [Theory]
    [InlineData("I='v(in)*.02'",.02)]
    [InlineData("I='v(in)*.02' M=10",.2)]
    [InlineData("I={v(in)*.02} M=10",.2)]
    public void Current_laws_emit_the_complete_scaled_transconductance(string law,double gain)
    {
        var plan=RequestedAnalysis.Prepare(DeckParser.Parse($"behavior\nV1 in 0 2\nR1 out 0 100\nB1 out 0 {law}\n.op\n.end\n",collectDeclarations:true));
        Assert.True(plan.Success,string.Join(',',plan.Diagnostics.Select(d=>d.Code)));
        Assert.Equal(gain,Assert.Single(plan.Operations[0].Requests[0].Circuit.Elements.OfType<Vccs>()).Siemens,12);
    }
    [Theory]
    [InlineData("I='v(in)' M=0")]
    [InlineData("I='v(in)' M=2 M=3")]
    [InlineData("V='v(in)' M=2")]
    [InlineData("I='v(in)' missing=2")]
    public void Invalid_tails_never_publish_a_partial_circuit(string law)
    {
        var plan=RequestedAnalysis.Prepare(DeckParser.Parse($"behavior\nV1 in 0 2\nR1 out 0 100\nB1 out 0 {law}\n.op\n.end\n",collectDeclarations:true));
        Assert.False(plan.Success);Assert.Empty(plan.Operations);
    }
}
