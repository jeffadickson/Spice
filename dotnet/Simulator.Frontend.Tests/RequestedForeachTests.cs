using Simulator.Syntax;
using Xunit;

namespace Simulator.Frontend.Tests;

public sealed class RequestedForeachTests
{
    private static DeckSyntax Deck(string commands)=>DeckParser.Parse(
        "Reporting loop\nV1 n1 0 1\nR1 n1 0 1k\n.control\n"+commands+"\n.endc\n.end\n",
        collectDeclarations:true);

    [Fact]
    public void A_terminal_loop_resolves_constructed_voltage_names_and_counts_every_item()
    {
        var syntax=Deck("op\nlet total = 0\nlet tests = 1+vector(2)\nforeach n $&tests\nset target = \"n{$n}\"\nif v($target) > 0\nlet total = total + 1\nend\nend\necho total=$&total\nquit 0");
        var plan=RequestedAnalysis.Prepare(syntax);
        Assert.True(plan.Success,string.Join(',',plan.Diagnostics.Select(d=>d.Code)));
        var report=ControlVectorExecution.Execute(syntax.Declarations!.ControlBlocks.Single().Commands.Skip(1),
            new Dictionary<string,double[]> {["v(n1)"]=[1],["v(n2)"]=[2]});
        Assert.Equal("total=2",Assert.Single(report.Output));
        Assert.Empty(report.Diagnostics);
    }

    [Theory]
    [InlineData("let tests = vector(2)\nforeach n $&tests\nop\nend")]
    [InlineData("op\nlet tests = vector(2)\nforeach n $&tests\nalter R1=7\nend")]
    [InlineData("op\nlet tests = vector(2)\nforeach n $&tests\nforeach m $&tests\nend\nend")]
    public void Physical_and_nested_loops_refuse(string commands)
    {
        Assert.Empty(RequestedAnalysis.Prepare(Deck(commands)).Operations);
    }
    [FsCheck.Xunit.Property(MaxTest=100,Replay="2668,20261107")]
    public bool A_reporting_loop_visits_the_requested_count(FsCheck.NonNegativeInt input)
    {
        var count=1+input.Get%64;
        var syntax=Deck("op\nlet total=0\nlet tests=vector("+count.ToString(System.Globalization.CultureInfo.InvariantCulture)+
            ")\nforeach n $&tests\nlet total = total + 1\nend\nquit 0");
        var plan=RequestedAnalysis.Prepare(syntax);
        if(!plan.Success)return false;
        var report=ControlVectorExecution.Execute(syntax.Declarations!.ControlBlocks.Single().Commands.Skip(1),
            new Dictionary<string,double[]>());
        return report.Locals["total"].Single()==count;
    }

    [Theory]
    [InlineData("let tests=vector(65)\nforeach n $&tests\nend","control_loop_items")]
    [InlineData("let tests=vector(1)\nforeach n $&tests\nset target=\"missing{$n}\"\nif v($target) > 0\nend\nend","unknown_control_vector:v(missing0)")]
    public void Bounds_and_missing_observables_fail_the_report_atomically(string commands,string code)
    {
        var syntax=Deck("op\n"+commands);
        Assert.True(RequestedAnalysis.Prepare(syntax).Success);
        var error=Assert.Throws<InvalidDataException>(()=>ControlVectorExecution.Execute(
            syntax.Declarations!.ControlBlocks.Single().Commands.Skip(1),new Dictionary<string,double[]>()));
        Assert.Equal(code,error.Message);
    }
}
