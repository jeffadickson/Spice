using Xunit;
namespace Simulator.Syntax.Tests;

/// <summary>Documented deterministic numparam functions; random functions remain outside this contract.</summary>
public sealed class ScalarFunctionTests
{
    [Theory]
    [InlineData("log(exp(1))",1)]
    [InlineData("log10(100)",2)]
    [InlineData("sqr(-3)",9)]
    [InlineData("pwr(-2,3)",8)]
    [InlineData("int(-1.9)",-1)]
    [InlineData("nint(2.5)",2)]
    [InlineData("nint(3.5)",4)]
    [InlineData("nint(-2.5)",-2)]
    [InlineData("ceil(-1.2)",-1)]
    [InlineData("floor(-1.2)",-2)]
    [InlineData("sgn(-4)",-1)]
    [InlineData("arctan(1)",0.7853981633974483)]
    [InlineData("asin(0)",0)]
    [InlineData("acos(1)",0)]
    [InlineData("atan(0)",0)]
    [InlineData("tan(0)",0)]
    [InlineData("sinh(0)",0)]
    [InlineData("cosh(0)",1)]
    [InlineData("tanh(0)",0)]
    [InlineData("asinh(0)",0)]
    [InlineData("acosh(1)",0)]
    [InlineData("atanh(0)",0)]
    public void Deterministic_functions_use_independent_mathematical_expected_values(string expression,double expected)
    {
        var catalog=DeckParser.Parse("Functions\n.param result={"+expression+"}\n.end\n",collectDeclarations:true).Declarations!;
        var result=ParameterEvaluator.Evaluate(catalog);
        Assert.True(result.Success,string.Join(',',result.Diagnostics.Select(d=>d.Code)));
        Assert.InRange(Math.Abs(result.Values["result"]-expected),0,1e-12);
    }
    [Theory]
    [InlineData("log(0)")]
    [InlineData("asin(2)")]
    [InlineData("acosh(.5)")]
    [InlineData("atanh(1)")]
    [InlineData("sqr(1e308)")]
    public void Nonfinite_function_domains_fail_atomically(string expression)
    {
        var catalog=DeckParser.Parse("Functions\n.param valid=42 result={"+expression+"}\n.end\n",collectDeclarations:true).Declarations!;
        var result=ParameterEvaluator.Evaluate(catalog);
        Assert.False(result.Success);Assert.Empty(result.Values);Assert.Contains(result.Diagnostics,d=>d.Code=="nonfinite_parameter");
    }
}
