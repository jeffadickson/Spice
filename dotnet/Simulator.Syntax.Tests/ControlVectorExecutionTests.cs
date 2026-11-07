using Simulator.Syntax;
using Xunit;

namespace Simulator.Syntax.Tests;

public sealed class ControlVectorExecutionTests
{
    [Fact]
    public void Evaluates_vector_function_ternary_and_substitution_in_only_the_taken_branch()
    {
        var syntax = DeckParser.Parse("report\nV1 out 0 1\n.control\nop\n"+
            "let data = 2*unitvec(3)\ndefine pick(a,b,c) a ? b : c\n"+
            "if length(pick(1,data,unitvec(2))) eq 3\n"+
            "let size = length(data)\necho \"size=$&size\"\nelse\necho \"bad=$&missing\"\nend\n.endc\n.end\n",
            collectDeclarations:true);
        var commands = Assert.Single(syntax.Declarations!.ControlBlocks).Commands;
        var report = ControlVectorExecution.Execute(commands.Skip(1), new Dictionary<string,double[]>());
        Assert.Equal("size=3", Assert.Single(report.Output));
        Assert.Equal(new[]{2d,2d,2d}, report.Locals["data"].ToArray());
    }

    [Theory]
    [InlineData(-0.25,"current")]
    [InlineData(0,"zero")]
    public void Reads_bound_branch_currents_and_tests_all_vector_elements(double last,string expected)
    {
        var syntax=DeckParser.Parse("report\nV1 out 0 1\n.control\n"+
            "if i(V1)\necho current\nelse\necho zero\nend\n.endc\n.end\n",collectDeclarations:true);
        var report=ControlVectorExecution.Execute(Assert.Single(syntax.Declarations!.ControlBlocks).Commands,
            new Dictionary<string,double[]> {["i(v1)"]=[0,last]});
        Assert.Equal(expected,Assert.Single(report.Output));
    }

    [Theory]
    [InlineData("let result = unitvec(4097)")]
    [InlineData("let result = missing+1")]
    [InlineData("echo \"$&missing\"")]
    [InlineData("let data=1\necho \"$&data[0]\"")]
    [InlineData("let data=1\nlet result = data > data")]
    [InlineData("shell echo unsafe")]
    [InlineData("if 1\necho partial")]
    public void Unsupported_or_incomplete_execution_is_refused(string commands)
    {
        var syntax=DeckParser.Parse("report\nV1 out 0 1\n.control\n"+commands+"\n.endc\n.end\n",collectDeclarations:true);
        Assert.Throws<InvalidDataException>(() => ControlVectorExecution.Execute(
            Assert.Single(syntax.Declarations!.ControlBlocks).Commands,new Dictionary<string,double[]>()));
    }
}
