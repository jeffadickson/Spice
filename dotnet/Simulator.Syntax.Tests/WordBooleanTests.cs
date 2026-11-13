using Simulator.Syntax;
using Xunit;
namespace Simulator.Syntax.Tests;

/// <summary>Word boolean operators are control-only aliases of existing operators.</summary>
public sealed class WordBooleanTests
{
    [Theory]
    [InlineData("1 or 0","yes")]
    [InlineData("0 and 1","no")]
    [InlineData("0 OR 1 AND 0","no")]
    [InlineData("1 or 1 and 0","yes")]
    [InlineData("2 > 1 or 1 > 2 or 2 > 3","yes")]
    public void Word_operators_preserve_existing_boolean_precedence(string condition,string expected)
    {
        var syntax=DeckParser.Parse("Words\n.control\nif "+condition+"\necho yes\nelse\necho no\nend\n.endc\n.end\n",collectDeclarations:true);
        var commands=Assert.Single(syntax.Declarations!.ControlBlocks).Commands;
        var report=ControlVectorExecution.Execute(commands,new Dictionary<string,double[]>());
        Assert.Equal(expected,Assert.Single(report.Output));
    }

    [Theory]
    [InlineData("^",8)]
    [InlineData("**",8)]
    public void Real_power_aliases_use_the_existing_expression_tree(string operation,double expected)
    {
        var report=Execute("let result=2"+operation+"3");
        Assert.Equal(expected,Assert.Single(report.Locals["result"]));
    }

    [Fact]
    public void Real_power_broadcasts_exponents_and_retains_sign_for_integer_powers()
    {
        var report=Execute("let result=data^2",new(){["data"]=[-2,0,3]});
        Assert.Equal(new[]{4d,0d,9d},report.Locals["result"].ToArray());
    }

    [Theory]
    [InlineData("(-2)^0.5")]
    [InlineData("0^(-1)")]
    [InlineData("1e300^2")]
    public void Nonfinite_real_power_is_refused(string expression)
        =>Assert.Equal("invalid_control_vector",Assert.Throws<InvalidDataException>(()=>Execute("let result="+expression)).Message);

    [Fact]
    public void Real_power_profile_refuses_nonreal_inputs()
    {
        var syntax=DeckParser.Parse("Power\n.control\nlet result=data^2\n.endc\n.end\n",collectDeclarations:true);
        var error=Assert.Throws<InvalidDataException>(()=>ControlVectorExecution.ExecuteComplex(
            Assert.Single(syntax.Declarations!.ControlBlocks).Commands,
            new Dictionary<string,System.Numerics.Complex[]> {["data"]=[System.Numerics.Complex.ImaginaryOne]}));
        Assert.Equal("control_real_required",error.Message);
    }
    private static ControlVectorReport Execute(string commands,Dictionary<string,double[]>? inputs=null)
    {
        var syntax=DeckParser.Parse("Power\n.control\n"+commands+"\n.endc\n.end\n",collectDeclarations:true);
        return ControlVectorExecution.Execute(Assert.Single(syntax.Declarations!.ControlBlocks).Commands,inputs??new());
    }
    [Fact]
    public void Ordinary_deck_names_remain_available()
    {
        var syntax=DeckParser.Parse("Names\n.param or=2 and=3\nR1 or and 1k\n.end\n",collectDeclarations:true);
        Assert.Equal("accepted_structure",syntax.Status);
        Assert.Equal(2,syntax.Declarations!.Parameters.Length);
    }
}
