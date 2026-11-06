using FsCheck;
using FsCheck.Xunit;
using Simulator.Syntax;
using Xunit;

namespace Simulator.Frontend.Tests;

public sealed class ControlSuffixTests
{
    private static string Text(string commands) =>
        "Control suffix\nV1 supply 0 1\nR1 supply out 1k\nR2 out 0 1k\n.control\n"+
        commands+"\n.endc\n.end\n";
    private static DeckSyntax Deck(string commands) => DeckParser.Parse(Text(commands),collectDeclarations:true);

    [Fact]
    public void Generated_suffix_declarations_keep_physical_spans_and_explicit_unexecuted_status()
    {
        const string commands="* command comment\nop\nlet twice = 2*v(out)\n* suffix comment\ndefine triple(x) 3*x\nlet report_value = triple(v(out))\nunlet twice\ndisplay\nlisting\nquit";
        var syntax=Deck(commands);
        var plan=RequestedAnalysis.Prepare(syntax);
        Assert.True(plan.Success,string.Join(',',plan.Diagnostics.Select(d=>d.Code)));
        Assert.Equal("op",Assert.Single(plan.Operations).Kind);
        Assert.Equal(4,plan.ProgramOmissions.Count(o=>o.Reason=="postprocessing_not_executed"));
        Assert.All(plan.ProgramOmissions,o=>{Assert.False(string.IsNullOrWhiteSpace(o.Kind));Assert.False(string.IsNullOrWhiteSpace(o.Name));});
        foreach(var omission in plan.ProgramOmissions.Where(o=>o.Reason=="postprocessing_not_executed"))
        {
            Assert.Equal("control_postprocessing",omission.Kind);
            Assert.Contains(omission.Name,Text(commands).Substring(omission.Range.Start,omission.Range.Length),StringComparison.Ordinal);
        }
        Assert.Equal(2,plan.ProgramOmissions.Count(o=>o.Reason=="control_output_not_executed"));
    }

    [Theory]
    [InlineData("let scratch=1\nop")]
    [InlineData("op\nlet scratch=1\nop")]
    [InlineData("op\ndefine scale(x) 2*x\nop")]
    [InlineData("op\nlet scratch=1\nrun")]
    [InlineData("op\nlet out=7")]
    [InlineData("op\nlet SUPPLY=7")]
    [InlineData("op\nlet time=7")]
    [InlineData("op\nlet frequency=7")]
    [InlineData("op\nlet all=7")]
    [InlineData("op\nlet local.name=7")]
    [InlineData("op\nlet v(out)=7")]
    [InlineData("op\nunlet out")]
    [InlineData("op\nunlet unknown")]
    [InlineData("op\nlet scratch=1\nunlet scratch\nunlet scratch")]
    [InlineData("op\nlet scratch=1\nunlet scratch scratch")]
    [InlineData("op\nlet scratch=1\nunlet scratch unknown")]
    [InlineData("op\ndefine scale(x,x) x")]
    [InlineData("op\ndefine scale(x) 1+")]
    [InlineData("op\nlet scratch=1+")]
    [InlineData("op\nlet scratch=$&supply")]
    [InlineData("op\nlet scratch=1; alter R1=5")]
    [InlineData("op\nlet scratch=1\nalter R1=5")]
    [InlineData("op\nquit\nlet scratch=1")]
    public void Ambiguous_or_effectful_controls_publish_no_partial_operations(string commands)
    {
        var plan=RequestedAnalysis.Prepare(Deck(commands));
        Assert.False(plan.Success);
        Assert.Empty(plan.Operations);
        Assert.All(plan.Diagnostics,d=>Assert.False(string.IsNullOrWhiteSpace(d.Code)));
    }

    [Theory]
    [InlineData("set xbrushwidth=1")]
    [InlineData("set xbrushwidth=64")]
    [InlineData("set xbrushwidth=2 numdgt=10")]
    public void Bounded_unsigned_plot_width_is_an_explicit_reporting_omission(string command)
    {
        var plan=RequestedAnalysis.Prepare(Deck(command+"\nop"));
        Assert.True(plan.Success,string.Join(',',plan.Diagnostics.Select(d=>d.Code)));
        Assert.Contains(plan.ProgramOmissions,o=>o.Reason=="reporting_setting_not_executed");
        Assert.Contains(plan.ProgramOmissions,o=>o.Kind=="control_reporting_setting"&&o.Name=="set");
    }

    [Theory]
    [InlineData("0")][InlineData("65")][InlineData("-2")][InlineData("+2")]
    [InlineData("2.5")][InlineData("{2}")][InlineData("2*1")][InlineData("abc")]
    public void Plot_setting_does_not_admit_arbitrary_values(string value)
    {
        var plan=RequestedAnalysis.Prepare(Deck("set xbrushwidth="+value+"\nop"));
        Assert.False(plan.Success);Assert.Empty(plan.Operations);
    }

    [Property(MaxTest=100,Replay="3404,20261105")]
    public bool Postprocessing_before_a_later_analysis_is_never_silently_ignored(PositiveInt value)
    {
        var plan=RequestedAnalysis.Prepare(Deck("op\nlet scratch="+(value.Get%1000)+"\nop"));
        return !plan.Success&&plan.Operations.IsEmpty;
    }

    [Theory][InlineData(64,true)][InlineData(65,false)]
    public void Local_name_budget_is_inclusive(int length,bool accepted)
    {
        var name=new string('n',length);
        var plan=RequestedAnalysis.Prepare(Deck("op\nlet "+name+"=2*v(out)"));
        Assert.Equal(accepted,plan.Success);
        if(!accepted)Assert.Empty(plan.Operations);
    }

    [Fact]
    public void Multiple_formals_and_local_removals_preserve_bounded_suffix_admission()
    {
        var plan=RequestedAnalysis.Prepare(Deck("op\ndefine pair(a,b) a+b\nlet _scratch1=pair(v(out),v(supply))\nlet scratch2=2*v(out)\nunlet _scratch1 scratch2"));
        Assert.True(plan.Success,string.Join(',',plan.Diagnostics.Select(d=>d.Code)));
        Assert.Equal(4,plan.ProgramOmissions.Count(o=>o.Reason=="postprocessing_not_executed"));
    }

    [Fact]
    public void Exact_analysis_budget_is_accepted_without_discarding_jobs()
    {
        var plan=RequestedAnalysis.Prepare(Deck(string.Concat(Enumerable.Repeat("op\n",128))));
        Assert.True(plan.Success,string.Join(',',plan.Diagnostics.Select(d=>d.Code)));Assert.Equal(128,plan.Operations.Length);
    }

    [Fact]
    public void Standalone_deck_profile_retains_original_analysis_order()
    {
        var syntax=DeckParser.Parse("Standalone profile\nV1 out 0 1\nR1 out 0 1k\n.op\n.dc V1 0 1 1\n.ac lin 2 10 20\n.end\n",collectDeclarations:true);
        var plan=RequestedAnalysis.Prepare(syntax);Assert.True(plan.Success);
        Assert.Equal("deck",plan.ControlProfile);Assert.Equal(new[]{"op","dc","ac"},plan.Operations.Select(o=>o.Kind));
    }

    [Fact]
    public void A_nested_control_envelope_is_an_explicit_scope_refusal()
    {
        var syntax=DeckParser.Parse("Nested control\n.subckt cell p\nR1 p 0 1k\n.control\nop\n.endc\n.ends cell\nV1 out 0 1\nX1 out cell\n.op\n.end\n",collectDeclarations:true);
        Assert.Equal("accepted_structure",syntax.Status);
        var plan=RequestedAnalysis.Prepare(syntax);
        Assert.False(plan.Success);Assert.Empty(plan.Operations);
        Assert.Contains(plan.Diagnostics,d=>d.Code=="unsupported_control_scope");
    }
    [Fact]
    public void Reporting_conditional_preserves_only_the_original_analysis()
    {
        var plan = RequestedAnalysis.Prepare(Deck(
            "op\nif abs(v(out)) <= 1\necho \"value=$&v(out)\"\nquit 0\nelse\nlet report_value = v(out)\nprint report_value\nend\nquit 1"));
        Assert.True(plan.Success, string.Join(',', plan.Diagnostics.Select(d => d.Code)));
        Assert.Equal("op", Assert.Single(plan.Operations).Kind);
        Assert.Contains(plan.ProgramOmissions, o => o.Reason == "conditional_reporting_not_executed");
    }

    [Theory]
    [InlineData("op\nif v(out)\nac lin 1 1k 1k\nend")]
    [InlineData("op\nif v(out)\nprint v(out) > result\nend")]
    [InlineData("op\nif v(out)\necho value | other\nend")]
    [InlineData("op\nif v(out)\nshell echo unsafe\nend")]
    [InlineData("op\nif v(out)\necho report")]
    [InlineData("op\nif v(out)\nelse\nelse\nend")]
    [InlineData("op\nif v(out)\nquit 0\nend\nop")]
    [InlineData("op\nif v(out)\nlet time = 1\nend")]
    public void Conditional_refuses_unbounded_or_analysis_affecting_commands(string commands)
        => Assert.False(RequestedAnalysis.Prepare(Deck(commands)).Success);

}
