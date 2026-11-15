using Simulator.Client;
using Simulator.Frontend;
using Simulator.Syntax;
using Xunit;
namespace Simulator.Frontend.Tests;
/// <summary>Independent polynomial laws exercise dynamic argument binding and lexical function bodies.</summary>
public sealed class DynamicFunctionVoltageTests
{
    private static RequestedAnalysisPlan Plan(string declarations,string law)=>RequestedAnalysis.Prepare(DeckParser.Parse($"dynamic functions\n{declarations}\nV1 in 0 2\nV2 other 0 3\nB1 out 0 V={{{law}}}\nR1 out 0 1000\n.dc V1 -2 2 1\n.end\n",collectDeclarations:true));
    private static double Value(BoundedExpression e,double x)=>e switch{
        ExpressionConstant c=>c.Value,ExpressionInput=>x,ExpressionAdd a=>Value(a.Left,x)+Value(a.Right,x),
        ExpressionMultiply m=>Value(m.Left,x)*Value(m.Right,x),ExpressionDivide d=>Value(d.Left,x)/Value(d.Right,x),
        ExpressionAbs a=>Math.Abs(Value(a.Argument,x)),ExpressionAsin a=>Math.Asin(Value(a.Argument,x)),ExpressionAcos a=>Math.Acos(Value(a.Argument,x)),ExpressionExp a=>Math.Exp(Value(a.Argument,x)),ExpressionLog a=>Math.Log(Value(a.Argument,x)),_=>throw new InvalidOperationException()};
    [Theory]
    [InlineData(".param a=5 b=3 c=4 d=2\n.func f(x) {a*x+b}\n.func g(x) {c*x*x+d}","f(g(v(in)))",20,13)]
    [InlineData(".param a=5 b=3 c=4 d=2\n.func f(x) {a*x+b}\n.func g(x) {c*x*x+d}\n.func fofg(x) {f(g(x))}","fofg(v(in))",20,13)]
    [InlineData(".param x=999\n.func square(x) {x*x}","square(v(in))",1,0)]
    [InlineData(".func exp(x) {x*x}","exp(v(in))",1,0)]
    [InlineData(".param a=10\n.func g(y) {a*y}\n.func f(a) {g(a)}","f(v(in))",1,0)]
    [InlineData(".func square(x,y) {x*x}","square(v(in),v(other))",1,0)]
    [InlineData(".param n=3\n.func f(x,n) {x**n}","f(v(in),2)",1,0)]
    public void Compiled_laws_match_independent_polynomials(string definitions,string law,double a,double b)
    {
        var plan=Plan(definitions,law);Assert.True(plan.Success,string.Join(',',plan.Diagnostics.Select(d=>d.Code)));
        var model=Assert.Single(plan.Operations[0].Requests[0].Circuit.Elements.OfType<BehavioralVoltage>());
        foreach(var x in new[]{-2d,-.5,0,.25,2})Assert.Equal(a*x*x+b,Value(model.Expression,x),10);
    }
    [Fact]
    public void Free_parameter_scope_follows_the_native_dynamic_macro_oracle()
    {
        var plan=RequestedAnalysis.Prepare(DeckParser.Parse("scope\n.param a=10\n.func f(x) {a*x*x}\nV1 in 0 2\nX1 in out cell\n.subckt cell in out\n.param a=20\nB1 out 0 V={f(v(in))}\nR1 out 0 1000\n.ends\n.op\n.end\n",collectDeclarations:true));
        Assert.True(plan.Success,string.Join(',',plan.Diagnostics.Select(d=>d.Code)));
        var model=Assert.Single(plan.Operations[0].Requests[0].Circuit.Elements.OfType<BehavioralVoltage>());
        Assert.Equal(80,Value(model.Expression,2));
    }
    [Theory]
    [InlineData(".func f(x) {f(x)}","f(v(in))")]
    [InlineData(".func f(x,x) {x*x}","f(v(in),v(in))")]
    [InlineData(".func f(x) {x*x}","f(v(in),v(other))")]
    [InlineData(".func f(x) {x*x}\n.func f(x) {2*x*x}","f(v(in))")]
    [InlineData(".func f(x,y) {x*y}","f(v(in),v(other))")]
    [InlineData(".func f(x) {time*x}","f(v(in))")]
    public void Unsupported_calls_return_no_partial_circuit(string definitions,string law)
    {var plan=Plan(definitions,law);Assert.False(plan.Success);Assert.Empty(plan.Operations);Assert.NotEmpty(plan.Diagnostics);}
    [Theory][InlineData("abs(v(in))")][InlineData("asin(v(in))")][InlineData("acos(v(in))")]
    public void Unary_generated_tree_matches_independent_library_values(string law){
        var p=Plan("",law);Assert.True(p.Success,string.Join(',',p.Diagnostics.Select(d=>d.Code)));var e=Assert.Single(p.Operations[0].Requests[0].Circuit.Elements.OfType<BehavioralVoltage>()).Expression;
        foreach(var x in new[]{-.9,-.25,0,.25,.9}){var expected=law.StartsWith("abs",StringComparison.Ordinal)?Math.Abs(x):law.StartsWith("asin",StringComparison.Ordinal)?Math.Asin(x):Math.Acos(x);Assert.Equal(expected,Value(e,x),12);}
    }
    [Theory][InlineData("v(in)**2",2)][InlineData("v(in)**-3",-3)]
    public void Integer_power_lowering_preserves_real_negative_bases(string law,int exponent){
        var p=Plan("",law);Assert.True(p.Success,string.Join(',',p.Diagnostics.Select(d=>d.Code)));var e=Assert.Single(p.Operations[0].Requests[0].Circuit.Elements.OfType<BehavioralVoltage>()).Expression;
        foreach(var x in new[]{-2d,-.5,.25,2})Assert.Equal(Math.Pow(x,exponent),Value(e,x),10);
    }}
