using FsCheck;
using FsCheck.Fluent;
using Microsoft.FSharp.Core;
using Simulator.Syntax;
using Xunit;
namespace Simulator.Frontend.Tests;
public sealed class AffineBehaviorTests
{
    private static BehavioralSourcePreparation Prepare(string card,string parameters="")
    {
        var syntax=DeckParser.Parse($"affine B\n{parameters}\n{card}\n.end\n",collectDeclarations:true);
        Assert.Equal("accepted_structure",syntax.Status);var catalog=syntax.Declarations!;
        return BehavioralSourceLowering.Prepare(catalog,Assert.Single(catalog.BehavioralSources),ParameterEvaluator.Evaluate(catalog));
    }
    [Theory]
    [InlineData("B1 out 0 V=2*v(in)+1",BehavioralQuantity.Voltage)]
    [InlineData("B1 out 0 I=2*v(in)+1",BehavioralQuantity.Current)]
    [InlineData("  b1 (out 0) v = {2*v(in)+1}",BehavioralQuantity.Voltage)]
    public void Generated_headers_and_affine_terms_keep_quantity_and_offsets(string card,BehavioralQuantity quantity)
    {
        var result=Prepare(card);Assert.True(result.Success,string.Join(',',result.Diagnostics.Select(d=>d.Code)));
        var source=result.Source!;Assert.Equal(quantity,source.Quantity);Assert.Equal("out",source.P.Text);Assert.Equal("0",source.N.Text);
        Assert.Equal(1,source.Offset);var term=Assert.Single(source.Terms);Assert.Equal("in",term.Node.Text);Assert.Equal(2,term.Coefficient);
        Assert.Equal(3,source.Range.Line);Assert.Equal(3,term.Node.Range.Line);
    }
    [Fact]
    public void Differential_references_parameters_and_linear_combination_are_exact()
    {
        var result=Prepare("B1 out ref V = bias + scale*(v(in,ref)-v(other))/2", ".param scale=4 bias=250m");
        Assert.True(result.Success);Assert.Equal(.25,result.Source!.Offset);
        var terms=result.Source.Terms.ToDictionary(t=>t.Node.Text,t=>t.Coefficient,StringComparer.OrdinalIgnoreCase);
        Assert.Equal(2,terms["in"]);Assert.Equal(-2,terms["ref"]);Assert.Equal(-2,terms["other"]);
    }
    [Theory]
    [InlineData("B1 out 0 V=3")]
    [InlineData("B1 out 0 V=v(in)-v(IN)+3")]
    [InlineData("B1 out 0 V=3+v(0)")]
    public void Constant_relations_and_cancelled_voltage_terms_have_no_hidden_terms(string card)
    {
        var result=Prepare(card);Assert.True(result.Success);Assert.Equal(3,result.Source!.Offset);Assert.Empty(result.Source.Terms);
    }
    [Theory]
    [InlineData("B1 out 0 V=v(in)*v(other)")]
    [InlineData("B1 out 0 V=1/v(in)")]
    [InlineData("B1 out 0 V=sin(v(in))")]
    [InlineData("B1 out 0 V=v(in)>0?1:0")]
    [InlineData("B1 out 0 V=v(in)^2")]
    [InlineData("B1 out 0 V=i(V1)")]
    [InlineData("B1 out 0 V=time")]
    [InlineData("B1 out 0 V=v(\"in\")")]
    [InlineData("B1 out 0 V=v(in) tc1=1")]
    public void Nonlinear_runtime_or_opaque_terms_are_atomic_refusals(string card)
    {
        var result=Prepare(card);Assert.False(result.Success);Assert.Null(result.Source);Assert.NotEmpty(result.Diagnostics);
    }
    [Fact]
    public void Nonfinite_and_missing_scalars_do_not_become_zero()
    {
        Assert.False(Prepare("B1 out 0 V=missing*v(in)").Success);
        Assert.False(Prepare("B1 out 0 V=1e308*v(in)*10").Success);
        Assert.False(Prepare("B1 out 0 V=v(in)/0").Success);
    }
    [Fact]
    public void Caller_overrides_and_lexical_parent_values_reach_the_affine_expression()
    {
        var parsed=DeckParser.Parse("t\n.param inherited=10\nX1 in out child k=4\n.subckt child p n k=2\nB1 n 0 V=k*v(p)+inherited+k\n.ends\n.end\n",collectDeclarations:true);
        var catalog=parsed.Declarations!;var call=Assert.Single(InstanceBinder.Bind(catalog).Instances);
        var environment=ParameterEvaluator.ForInstance(catalog,call,ParameterEvaluator.Evaluate(catalog));
        var result=BehavioralSourceLowering.Prepare(catalog,Assert.Single(catalog.BehavioralSources),environment);
        Assert.True(result.Success);Assert.Equal(14,result.Source!.Offset);Assert.Equal(4,Assert.Single(result.Source.Terms).Coefficient);
    }
    [Fact]
    public void Continuation_ranges_retain_the_physical_voltage_node_line()
    {
        var result=Prepare("B1 out 0 V=2*\n+ v(in)+1");Assert.True(result.Success);
        Assert.Equal(4,Assert.Single(result.Source!.Terms).Node.Range.Line);
    }
    [Fact]
    public void Term_and_depth_budgets_refuse_without_partial_output()
    {
        var terms=Prepare("B1 out 0 V="+string.Join('+',Enumerable.Range(1,17).Select(i=>$"v(n{i})")));
        Assert.False(terms.Success);Assert.Equal("behavior_term_budget",Assert.Single(terms.Diagnostics).Code);
        Assert.False(Prepare("B1 out 0 V="+new string('(',66)+"v(in)"+new string(')',66)).Success);
    }
    [Fact]
    public void Foreign_environments_and_reserved_runtime_names_are_refused()
    {
        var parsed=DeckParser.Parse("t\nB1 out 0 V=v(in)\n.end\n",collectDeclarations:true);var catalog=parsed.Declarations!;
        var foreign=DeckParser.Parse("t\n.end\n",collectDeclarations:true).Declarations!;
        Assert.False(BehavioralSourceLowering.Prepare(catalog,Assert.Single(catalog.BehavioralSources),ParameterEvaluator.Evaluate(foreign)).Success);
        Assert.False(Prepare("B1 out 0 V=time+v(in)",".param time=3").Success);
    }
    [Fact]
    public void Affine_scalar_property_campaign()
    {
        var config=Config.QuickThrowOnFailure.WithMaxTest(128).WithReplay(FSharpOption<Replay>.Some(new(new Rnd(20261103UL),null)));
        Check.One(config,Prop.ForAll<short,short,short>((a,b,c)=>{
            var result=Prepare($"B1 out 0 V={a}*v(in)+({b})*v(ref)+({c})");
            if(!result.Success||result.Source!.Offset!=c)return false;
            var terms=result.Source.Terms.ToDictionary(t=>t.Node.Text,t=>t.Coefficient);
            return terms.GetValueOrDefault("in")==a&&terms.GetValueOrDefault("ref")==b;
        }));
    }
}
