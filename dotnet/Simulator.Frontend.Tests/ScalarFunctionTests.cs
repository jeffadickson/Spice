using Simulator.Client;
using Simulator.Frontend;
using Simulator.Syntax;
using Xunit;
namespace Simulator.Frontend.Tests;
/// <summary>Scalar user functions share parameter resolution, lexical definitions and bounded recursion.</summary>
public sealed class ScalarFunctionTests
{
    [Fact]
    public void Nested_function_calls_bind_formals_before_parameters()
    {
        var syntax=DeckParser.Parse("functions\n.param x=999\n.func twice(x) {2*x}\n.func nested(y) {twice(y)+1}\nV1 in 0 5\nR1 in 0 {nested(2)*1000}\n.dc V1 0 5 1\n.end\n",collectDeclarations:true);
        var plan=RequestedAnalysis.Prepare(syntax);Assert.True(plan.Success,string.Join(',',plan.Diagnostics.Select(d=>d.Code)));
        Assert.Equal(5000,Assert.Single(plan.Operations[0].Requests[0].Circuit.Elements.OfType<Resistor>()).Ohms);
    }
    [Fact]
    public void Nearest_function_definition_shadows_ancestor()
    {
        var plan=RequestedAnalysis.Prepare(DeckParser.Parse("functions\n.func scale(x) {10*x}\nV1 in 0 5\nX1 in 0 cell r=2000\n.subckt cell a b r=1000\n.func scale(x) {5*x}\nR1 a b {scale(r)}\n.ends\n.op\n.end\n",collectDeclarations:true));
        Assert.True(plan.Success,string.Join(',',plan.Diagnostics.Select(d=>d.Code)));Assert.Equal(10000,Assert.Single(plan.Operations[0].Requests[0].Circuit.Elements.OfType<Resistor>()).Ohms);
    }
    [Fact]
    public void Macro_parameters_and_nested_formals_match_native_ngspice()
    {
        // Independently checked with ngspice 47+: conductance is 1/40+1/3.
        var plan=RequestedAnalysis.Prepare(DeckParser.Parse("macro\n.param p=10 x=10\n.func f(a) {p*a}\n.func g(y) {x+y}\n.func h(x) {g(1)}\nV1 in 0 1\nX1 in 0 cell\n.subckt cell a b\n.param p=20\nR1 a b {f(2)}\nR2 a b {h(2)}\n.ends\n.op\n.end\n",collectDeclarations:true));
        Assert.True(plan.Success,string.Join(',',plan.Diagnostics.Select(d=>d.Code)));
        Assert.Equal(new[]{3d,40d},plan.Operations[0].Requests[0].Circuit.Elements.OfType<Resistor>().Select(r=>r.Ohms).Order().ToArray());
    }
    [Fact]
    public void Nested_function_names_use_the_definition_namespace()
    {
        // Native ngspice selects root g even though the child defines another g.
        var plan=RequestedAnalysis.Prepare(DeckParser.Parse("macro\n.func g(y) {10*y}\n.func f(x) {g(x)}\nV1 in 0 1\nX1 in 0 cell\n.subckt cell a b\n.func g(y) {20*y}\nR1 a b {f(2)}\n.ends\n.op\n.end\n",collectDeclarations:true));
        Assert.True(plan.Success,string.Join(',',plan.Diagnostics.Select(d=>d.Code)));
        Assert.Equal(20,Assert.Single(plan.Operations[0].Requests[0].Circuit.Elements.OfType<Resistor>()).Ohms);
    }
    [Fact]
    public void Randomized_scalar_functions_preserve_formal_precedence()
    {
        var config=FsCheck.Config.QuickThrowOnFailure.WithMaxTest(128).WithReplay(Microsoft.FSharp.Core.FSharpOption<FsCheck.Replay>.Some(new(new FsCheck.Rnd(2026111503UL),null)));
        FsCheck.Check.One(config,FsCheck.Fluent.Prop.ForAll<FsCheck.NonNegativeInt>(size=>{
            var x=1+size.Get%100;
            var plan=RequestedAnalysis.Prepare(DeckParser.Parse($"property\n.param x=999\n.func f(x) {{2*x+1}}\nV1 in 0 1\nR1 in 0 {{f({x})}}\n.op\n.end\n",collectDeclarations:true));
            return plan.Success&&plan.Operations[0].Requests[0].Circuit.Elements.OfType<Resistor>().Single().Ohms==2*x+1;
        }));
    }
    [Theory]
    [InlineData(32,true)]
    [InlineData(33,false)]
    public void Function_depth_has_an_explicit_acceptance_boundary(int count,bool accepted)
    {
        var definitions=string.Join('\n',Enumerable.Range(0,count).Select(i=>$".func f{i}(x) {{{(i==count-1?"x":$"f{i+1}(x)")}}}"));
        var plan=RequestedAnalysis.Prepare(DeckParser.Parse($"depth\n{definitions}\nV1 in 0 1\nR1 in 0 {{f0(2)}}\n.op\n.end\n",collectDeclarations:true));
        Assert.Equal(accepted,plan.Success);
        if(!accepted){Assert.Empty(plan.Operations);Assert.Contains(plan.Diagnostics,d=>d.Code=="function_depth");}
    }
    [Theory]
    [InlineData(".func f(x) {f(x)}","f(1)")]
    [InlineData(".func f(x) {x}","f(1,2)")]
    [InlineData(".func f(x,x) {x}","f(1,2)")]
    [InlineData(".func f(x) {x}\n.func f(x) {2*x}","f(1)")]
    public void Invalid_function_calls_are_atomic(string declarations,string call)
    {
        var plan=RequestedAnalysis.Prepare(DeckParser.Parse($"functions\n{declarations}\nV1 in 0 1\nR1 in 0 {{{call}}}\n.op\n.end\n",collectDeclarations:true));
        Assert.False(plan.Success);Assert.Empty(plan.Operations);
    }
}
