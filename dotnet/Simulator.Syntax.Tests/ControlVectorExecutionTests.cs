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
    [InlineData("let result = 1 > 2")]
    [InlineData("shell echo unsafe")]
    [InlineData("if 1\necho partial")]
    public void Unsupported_or_incomplete_execution_is_refused(string commands)
    {
        var syntax=DeckParser.Parse("report\nV1 out 0 1\n.control\n"+commands+"\n.endc\n.end\n",collectDeclarations:true);
        Assert.Throws<InvalidDataException>(() => ControlVectorExecution.Execute(
            Assert.Single(syntax.Declarations!.ControlBlocks).Commands,new Dictionary<string,double[]>()));
    }
    [Fact]
    public void Prefix_variables_missing_conditions_and_reverse_inequality_match_native_branches()
    {
        var syntax=DeckParser.Parse("report\nV1 out 0 1\n.control\n"+
            "set local_empty=\"\"\nset local_flag\nif\necho bad\nelse\necho missing\nend\n"+
            "if $local_empty\necho bad\nend\nif $local_flag\necho \"flag=$local_flag\"\nend\n"+
            "if 1 >< 2\necho unequal\nend\n.endc\n.end\n",collectDeclarations:true);
        var report=ControlVectorExecution.Execute(Assert.Single(syntax.Declarations!.ControlBlocks).Commands,
            new Dictionary<string,double[]>());
        Assert.Equal(new[]{"missing","flag=TRUE","unequal"},report.Output.ToArray());
    }

    [Fact]
    public void Numeric_voltage_name_and_redirect_return_owned_data()
    {
        var syntax=DeckParser.Parse("report\nV1 1 0 42\n.control\n"+
            "print v(1)\nlet zero=0\nprint zero > capture\n.endc\n.end\n",collectDeclarations:true);
        var report=ControlVectorExecution.Execute(Assert.Single(syntax.Declarations!.ControlBlocks).Commands,
            new Dictionary<string,double[]> {["v(1)"]=[42]});
        Assert.Equal("v(1) = 42",Assert.Single(report.Output));
        Assert.Equal("zero = 0.000000e+00",report.Redirected["capture"]);
    }

    [Theory]
    [InlineData("test.log")]
    [InlineData("analysis-results.txt")]
    public void Redirect_accepts_a_safe_leaf_filename_as_owned_data(string target)
    {
        var syntax=DeckParser.Parse("report\nV1 out 0 1\n.control\nlet zero=0\nprint zero > "+target+
            "\n.endc\n.end\n",collectDeclarations:true);
        var report=ControlVectorExecution.Execute(Assert.Single(syntax.Declarations!.ControlBlocks).Commands,
            new Dictionary<string,double[]>());
        Assert.Equal("zero = 0.000000e+00",report.Redirected[target]);
    }

    [Theory]
    [InlineData("print zero > ../escape")]
    [InlineData("print zero >> capture")]
    [InlineData("print zero > capture; shell")]
    [InlineData("print zero > a..log")]
    [InlineData("print zero > .hidden")]
    [InlineData("print zero > a.")]
    [InlineData("print zero > C:escape")]
    public void Redirect_rejects_paths_append_and_separators(string command)
    {
        var syntax=DeckParser.Parse("report\nV1 out 0 1\n.control\nlet zero=0\n"+
            command+"\n.endc\n.end\n",collectDeclarations:true);
        Assert.Throws<InvalidDataException>(()=>ControlVectorExecution.Execute(
            Assert.Single(syntax.Declarations!.ControlBlocks).Commands,new Dictionary<string,double[]>()));
    }

    [Fact]
    public void Analysis_bound_execution_requires_a_dispatch_marker()
    {
        Assert.Throws<InvalidDataException>(()=>ControlVectorExecution.Execute(
            [],new Dictionary<string,double[]>(),activateAtAnalysis:true));
    }

}
