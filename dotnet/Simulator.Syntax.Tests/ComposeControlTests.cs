using Simulator.Syntax;
using FsCheck;
using FsCheck.Xunit;
using System.Globalization;
using System.Collections.Immutable;
using Xunit;

namespace Simulator.Syntax.Tests;

public sealed class ComposeControlTests
{
    [Property(MaxTest=128,Replay="385,20261111")]
    public bool Composition_preserves_order_and_sign_across_bounded_lengths(NonNegativeInt input)
    {
        var expected=Enumerable.Range(0,input.Get%32+1).Select(i=>(double)(input.Get%100-50+i)).ToArray();
        var items=string.Join(",",expected.Select(value=>"("+value.ToString("R",CultureInfo.InvariantCulture)+")"));
        var report=Execute("compose actual values "+items);
        return report.Locals["actual"].SequenceEqual(expected);
    }

    private static ControlVectorReport Execute(string body, Dictionary<string,double[]>? inputs=null)
    {
        var syntax=DeckParser.Parse("compose\nV1 n1 0 2.7\n.control\n"+body+"\n.endc\n.end\n",collectDeclarations:true);
        return ControlVectorExecution.Execute(Assert.Single(syntax.Declarations!.ControlBlocks).Commands,
            inputs??new Dictionary<string,double[]>());
    }

    [Fact]
    public void Compose_evaluates_original_log_fixture_expressions_in_order()
    {
        var report=Execute("compose actual values v(n1) ln(2.7) log(2.7) log10(2.7)",new(){["v(n1)"]=[2.7]});
        Assert.Empty(report.Diagnostics);
        // Independent constants retained by the original ngspice regression.
        var expected=new[]{2.7,0.9932517730102834,0.9932517730102834,0.43136376415898736};
        Assert.All(report.Locals["actual"].Zip(expected),pair=>Assert.InRange(Math.Abs(pair.First-pair.Second),0,1e-15));
    }

    [Fact]
    public void Adjacent_expressions_preserve_native_binary_minus_and_comma_boundaries()
    {
        var report=Execute("compose actual values 1 -2 3, (4+5)\ncompose copied values actual[0] actual[1]");
        Assert.Equal(new[]{-1d,3d,9d},report.Locals["actual"].ToArray());
        Assert.Equal(new[]{-1d,3d},report.Locals["copied"].ToArray());
    }

    [Fact]
    public void Only_the_taken_branch_publishes_a_composed_vector_and_replacement_is_owned()
    {
        var report=Execute("let actual=99\nif 0\ncompose actual values missing\nelse\ncompose actual values 2 3\nend\nunlet actual\ncompose actual values 4");
        Assert.Equal(new[]{4d},report.Locals["actual"].ToArray());
    }

    [Theory]
    [InlineData("compose actual values unitvec(2)")]
    [InlineData("compose actual values missing")]
    [InlineData("compose actual values ln(-1)")]
    [InlineData("compose actual values ln(0)")]
    [InlineData("let data=vector(3)\ncompose actual values data[-1]")]
    [InlineData("let data=vector(3)\ncompose actual values data[3]")]
    [InlineData("let data=vector(3)\ncompose actual values data[0.5]")]
    [InlineData("compose actual values")]
    [InlineData("compose actual lin=3 start=0 stop=1")]
    public void Invalid_or_unimplemented_forms_publish_no_result(string body)
        =>Assert.Throws<InvalidDataException>(()=>Execute(body));

    [Fact]
    public void Compose_cannot_overwrite_caller_inputs()
        =>Assert.Throws<InvalidDataException>(()=>Execute("compose actual values 3",new(){["actual"]=[1]}));

    [Fact]
    public void Unresolved_hyphen_tokens_use_bounded_arithmetic_but_exact_names_win()
    {
        var report=Execute("compose gold values 1 2 3\nlet delta=gold-1\ncompose picked values delta[0] delta[2]",new(){["real-name"]=[42]});
        Assert.Equal(new[]{0d,1d,2d},report.Locals["delta"].ToArray());
        var exact=Execute("compose picked values real-name",new(){["real-name"]=[42], ["real"]=[100], ["name"]=[50]});
        Assert.Equal(42,Assert.Single(exact.Locals["picked"]));
    }

    [Fact]
    public void Unspaced_subtraction_keeps_division_precedence_and_voltage_names()
    {
        var report=Execute("compose gold values 1 2 3\nlet err=vecmax(abs(gold/gold-1))\ncompose picked values v(real-name)",
            new(){["v(real-name)"]=[42], ["real"]=[100], ["name"]=[50]});
        Assert.Equal(0,Assert.Single(report.Locals["err"]));
        Assert.Equal(42,Assert.Single(report.Locals["picked"]));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(4097)]
    public void Typed_commands_cannot_bypass_compose_value_budgets(int count)
    {
        var syntax=DeckParser.Parse("compose\n.control\ncompose actual values 1\n.endc\n.end\n",collectDeclarations:true);
        var command=Assert.Single(Assert.Single(syntax.Declarations!.ControlBlocks).Commands);
        var declaration=command.Postprocessing!;
        var forged=command with{Postprocessing=declaration with{Values=Enumerable.Repeat(declaration.Values[0],count).ToImmutableArray()}};
        var error=Assert.Throws<InvalidDataException>(()=>ControlVectorExecution.Execute([forged],new Dictionary<string,double[]>()));
        Assert.Equal("control_compose_value_budget",error.Message);
    }
}
