using Simulator.RequestedQualification;
using Simulator.Syntax;
using Xunit;

namespace Simulator.Frontend.Tests;

public sealed class RequestedBatchControlTests
{
    private const string Deck = """
        batch profile
        V1 in 0 1
        R1 in out 1k
        C1 out 0 1u
        .tran 100u 10m uic
        .control
        if $?batchmode
          echo "batch"
        else
          echo "interactive"
          unset ngdebug
          tran 200u 10m uic
          plot v(out)
        end
        .endc
        .end
        """;

    [Fact]
    public void Batch_branch_retains_root_analysis_and_masks_interactive_program()
    {
        var syntax = DeckParser.Parse(Deck, collectDeclarations:true);
        var plan = RequestedAnalysis.Prepare(syntax);
        Assert.True(plan.Success,string.Join(',',plan.Diagnostics.Select(d=>d.Code)));
        Assert.Equal("batchmode_root",plan.ControlProfile);
        var operation=Assert.Single(plan.Operations);
        Assert.Null(operation.ControlOrigin);
        Assert.Equal(0.0001,operation.Arguments[0],12);
        var projection=OriginalProjection.Create(Deck,syntax,operation,plan);
        Assert.Contains(".tran 100u 10m uic",projection.Text);
        Assert.DoesNotContain("tran 200u",projection.Text);
        Assert.DoesNotContain("unset ngdebug",projection.Text);
        Assert.False(projection.RequiresControlWrite);
        Assert.Contains(projection.Omissions,o=>o.Reason=="batchmode_reporting_and_interactive_branch_not_executed");
    }

    [Theory]
    [InlineData("echo \"batch\"","shell exit")]
    [InlineData("unset ngdebug","unset batchmode")]
    [InlineData("if $?batchmode","if $?interactive")]
    [InlineData("echo \"batch\"","tran 200u 10m uic")]
    [InlineData("plot v(out)","quit")]
    [InlineData("end\n.endc","end\nrun\n.endc")]
    public void Unknown_effects_and_other_conditions_do_not_enter_batch_profile(string from,string to)
    {
        var text=Deck.Replace(from,to,StringComparison.Ordinal);
        Assert.NotEqual(Deck,text);
        var plan=RequestedAnalysis.Prepare(DeckParser.Parse(text,collectDeclarations:true));
        Assert.False(plan.Success);
    }
}
