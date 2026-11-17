using Simulator.Client;
using Simulator.Frontend;
using Simulator.Syntax;
using Xunit;
using FsCheck;
namespace Simulator.Frontend.Tests;

/// <summary>Independent affine laws and scoped parameters, not generated-parser implementation mirrors.</summary>
public sealed class ControlledCurrentSourceTests
{
    private static RequestedAnalysisPlan Prepare(string source)=>RequestedAnalysis.Prepare(DeckParser.Parse(source,collectDeclarations:true));
    private static string Deck(string source)=>$"current source\nV1 in 0 2\nR1 out 0 100\n{source}\n.op\n.end\n";

    [Theory]
    [InlineData("G1 out 0 in 0 .02 M=10")]
    [InlineData("G1 out 0 CUR='v(in)*.02' M=10")]
    public void Explicit_multiplier_scales_the_physical_transconductance(string source)
    {
        var plan=Prepare(Deck(source));Assert.True(plan.Success,string.Join(',',plan.Diagnostics.Select(d=>d.Code)));
        var g=Assert.Single(plan.Operations[0].Requests[0].Circuit.Elements.OfType<Vccs>());
        Assert.Equal(.2,g.Siemens,12);
    }

    [Fact]
    public void Differential_current_law_scales_every_coefficient_and_offset()
    {
        var plan=Prepare(Deck("V2 other 0 1\nG1 out 0 CUR='2m+3m*v(in,other)' M=4"));
        Assert.True(plan.Success,string.Join(',',plan.Diagnostics.Select(d=>d.Code)));
        var elements=plan.Operations[0].Requests[0].Circuit.Elements;
        // Differential affine laws retain both scaled coefficients in the
        // internal voltage evaluator, followed by one unit transconductance.
        Assert.Equal(new[]{-.012,.012},elements.OfType<Vcvs>().Select(g=>g.Gain).Order().ToArray());
        Assert.Equal(1,Assert.Single(elements.OfType<Vccs>()).Siemens);
        Assert.Contains(elements,e=>e is Current {Amps:.008});
    }

    [Theory]
    [InlineData("G1 a b CUR='v(c,d)*.02' M='M'",.2)]
    [InlineData("G1 a b CUR='v(c,d)*.02'",.02)]
    public void Formal_M_is_lexically_evaluated_without_implicit_scaling(string body,double expected)
    {
        var plan=Prepare($"hierarchy\nV1 in 0 2\nR1 out 0 100\nX1 out 0 in 0 cell M=10\n.subckt cell a b c d M=1\n{body}\n.ends\n.op\n.end\n");
        Assert.True(plan.Success,string.Join(',',plan.Diagnostics.Select(d=>d.Code)));
        Assert.Equal(expected,Assert.Single(plan.Operations[0].Requests[0].Circuit.Elements.OfType<Vccs>()).Siemens,12);
    }

    [Theory]
    [InlineData("G1 out 0 in 0 .02 M=0")]
    [InlineData("G1 out 0 in 0 .02 M=-1")]
    [InlineData("G1 out 0 in 0 1e308 M=10")]
    [InlineData("G1 out 0 in 0 .02 M=2 M=3")]
    [InlineData("G1 out 0 in 0 .02 gain=2")]
    [InlineData("G1 out 0 CUR='v(in)*v(in)' M=2")]
    [InlineData("G1 out 0 CUR='v(in)' M=2 extra=1")]
    [InlineData("G1 out 0 CUR='v(in)' M=0")]
    [InlineData("G1 out 0 CUR='v(in)' M=1e309")]
    [InlineData("G1 out 0 CUR='v(in)' M=2 M=3")]
    [InlineData("G1 out 0 CUR='' M=2")]
    [InlineData("G1 out 0 CUR='v(in)' M=missing")]
    [InlineData("E1 out 0 in 0 2 M=3")]
    public void Unsupported_or_invalid_laws_publish_no_partial_requests(string source)
    {
        var plan=Prepare(Deck(source));Assert.False(plan.Success);Assert.Empty(plan.Operations);Assert.NotEmpty(plan.Diagnostics);
    }

    [Fact]
    public void Signed_affine_coefficients_and_offsets_scale_together_for_fractional_multipliers()
    {
        var config=Config.QuickThrowOnFailure.WithMaxTest(128).WithReplay(
            Microsoft.FSharp.Core.FSharpOption<Replay>.Some(new(new Rnd(2026111501UL),null)));
        Check.One(config,FsCheck.Fluent.Prop.ForAll<NonNegativeInt,int>( (size,signed)=>{
            var factor=(1+size.Get%128)*.25;
            var gain=(signed%201)/1000d;if(gain==0)gain=.001;
            var offset=(signed%97)/10000d;
            var plan=Prepare(Deck(FormattableString.Invariant($"G1 out 0 CUR='({gain})*v(in)+({offset})' M={factor}")));
            if(!plan.Success)return false;
            var elements=plan.Operations[0].Requests[0].Circuit.Elements;
            var g=elements.OfType<Vccs>().Single();var current=elements.OfType<Current>().Single();
            return Math.Abs(g.Siemens-gain*factor)<1e-12&&Math.Abs(current.Amps-offset*factor)<1e-12;
        }));
    }
}
