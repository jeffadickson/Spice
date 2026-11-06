using System.Collections.Immutable;
using Simulator.Syntax;
using Xunit;

namespace Simulator.Syntax.Tests;

public sealed class GrammarImplementationTests
{
    private static GrammarRuleAssessment Assessment(string rule)=>new(rule,
        GrammarStageStatus.Bounded,GrammarStageStatus.Bounded,GrammarStageStatus.NotExecuted,
        ["dotnet/Simulator.Syntax/ControlExtraction.cs"],["FRONTEND-CONTROL-006"],
        ["dotnet/Simulator.Frontend.Tests/ControlSuffixTests.cs"],"Reporting suffix only; evaluation is unavailable.");

    [Fact]
    public void Unmapped_generated_rules_remain_visible_without_becoming_execution_support()
    {
        var rows=GrammarImplementationInventory.Build([]);
        Assert.True(rows.Length>50);
        foreach(var rule in new[]{"controlUnknown","jfet","osdi","expression"})
        {
            var row=Assert.Single(rows,r=>r.Rule==rule);
            Assert.False(row.Assessed);Assert.Equal(GrammarStageStatus.Unassessed,row.Execution);
            Assert.False(string.IsNullOrWhiteSpace(row.Limitation));
        }
    }

    [Fact]
    public void Recognized_declaration_with_metadata_does_not_become_an_executed_command()
    {
        var rows=GrammarImplementationInventory.Build([Assessment("controlLet")]);
        var row=Assert.Single(rows,r=>r.Rule=="controlLet");
        Assert.True(row.Assessed);Assert.Equal(GrammarStageStatus.NotExecuted,row.Execution);
        Assert.Equal("FRONTEND-CONTROL-006",Assert.Single(row.Requirements));
        Assert.False(Assert.Single(rows,r=>r.Rule=="controlUnknown").Assessed);
    }

    [Fact]
    public void Stale_duplicate_or_unsupported_assessment_claims_refuse_instead_of_disappearing()
    {
        AssertInvalid([Assessment("invented_rule")]);
        AssertInvalid([Assessment("controlOp"),Assessment("controlOp")]);
        AssertInvalid([Assessment("controlOp") with{Evidence=[]}]);
        AssertInvalid([Assessment("controlOp") with{Execution=(GrammarStageStatus)999}]);
        AssertInvalid([Assessment("controlOp") with{Commands=["op"]},Assessment("controlAc") with{Commands=["OP"]}]);
        AssertInvalid([Assessment("controlOp") with{Commands=["op","OP"]}]);
        AssertInvalid([null!]);
    }

    [Fact]
    public void Assessment_input_order_cannot_change_the_generated_inventory()
    {
        var first=Assessment("controlLet");var second=Assessment("controlDefine");
        var forward=GrammarImplementationInventory.Build([first,second]);
        var reverse=GrammarImplementationInventory.Build([second,first]);
        Assert.Equal(System.Text.Json.JsonSerializer.Serialize(forward),System.Text.Json.JsonSerializer.Serialize(reverse));
    }

    [Theory]
    [InlineData("implementation")][InlineData("requirements")][InlineData("evidence")]
    [InlineData("blank_implementation")][InlineData("blank_requirement")][InlineData("blank_evidence")]
    [InlineData("limitation")][InlineData("extraction")][InlineData("preparation")]
    [InlineData("command")][InlineData("default_commands")]
    [InlineData("mixed_implementation")][InlineData("mixed_requirement")][InlineData("mixed_evidence")]
    [InlineData("embedded_space")]
    public void Incomplete_public_assessment_data_cannot_be_published(string field)
    {
        var candidate=Assessment("controlOp");
        candidate=field switch {
            "implementation"=>candidate with{Implementation=[]},
            "requirements"=>candidate with{Requirements=[]},
            "evidence"=>candidate with{Evidence=[]},
            "blank_implementation"=>candidate with{Implementation=[" "]},
            "blank_requirement"=>candidate with{Requirements=[""]},
            "blank_evidence"=>candidate with{Evidence=["\t"]},
            "limitation"=>candidate with{Limitation=" "},
            "extraction"=>candidate with{Extraction=(GrammarStageStatus)999},
            "preparation"=>candidate with{Preparation=(GrammarStageStatus)999},
            "command"=>candidate with{Commands=[" "]},
            "default_commands"=>candidate with{Commands=default},
            "mixed_implementation"=>candidate with{Implementation=["source.cs"," "]},
            "mixed_requirement"=>candidate with{Requirements=["REQ-001",""]},
            "mixed_evidence"=>candidate with{Evidence=["tests.cs","\t"]},
            "embedded_space"=>candidate with{Commands=["op extra"]},
            _=>throw new ArgumentException("Unknown fixture field")
        };
        AssertInvalid([candidate]);
    }

    [Fact]
    public void Valid_concrete_commands_are_retained_in_the_published_row()
    {
        var rows=GrammarImplementationInventory.Build([Assessment("controlOp") with{Commands=["op"]}]);
        Assert.Equal("op",Assert.Single(Assert.Single(rows,r=>r.Rule=="controlOp").Commands));
        Assert.Throws<ArgumentNullException>(()=>GrammarImplementationInventory.Build(null!));
    }

    private static void AssertInvalid(IEnumerable<GrammarRuleAssessment> candidates)
    {
        var error=Assert.Throws<InvalidDataException>(()=>GrammarImplementationInventory.Build(candidates));
        Assert.False(string.IsNullOrWhiteSpace(error.Message));
    }

    [Fact]
    public void Every_registered_control_heading_has_a_real_generated_metadata_witness()
    {
        var options=new System.Text.Json.JsonSerializerOptions{PropertyNamingPolicy=System.Text.Json.JsonNamingPolicy.SnakeCaseLower};
        options.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter(System.Text.Json.JsonNamingPolicy.SnakeCaseLower));
        var assessments=System.Text.Json.JsonSerializer.Deserialize<GrammarRuleAssessment[]>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"grammar-implementation.json")),options)!;
        var witnesses=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase) {
            ["op"]="op",["ac"]="ac lin 2 10 20",["dc"]="dc V1 0 1 1",["tran"]="tran 1u 2u",["run"]="run",
            ["set"]="set numdgt=7",["let"]="let scratch=2*v(out)",["define"]="define scale(x) 2*x",["unlet"]="unlet scratch",
            ["print"]="print v(out)",["plot"]="plot v(out)",["meas"]="meas tran sample max v(out)",["measure"]="measure tran sample max v(out)",
            ["save"]="save v(out)",["gnuplot"]="gnuplot output v(out)",["echo"]="echo result",["if"]="if v(out) < 1",["else"]="else",["end"]="end",["rusage"]="rusage",["display"]="display",["listing"]="listing",["quit"]="quit"
        };
        foreach(var assessment in assessments)
        foreach(var heading in assessment.Commands)
        {
            Assert.True(witnesses.TryGetValue(heading,out var command),"Missing executable metadata witness: "+heading);
            var syntax=DeckParser.Parse("Command witness\nV1 out 0 1\n.control\n"+command+"\n.endc\n.end\n",collectDeclarations:true);
            var actual=Assert.Single(Assert.Single(syntax.Declarations!.ControlBlocks).Commands);
            Assert.Null(actual.Diagnostic);Assert.NotEqual("unsupported",actual.Kind);Assert.Equal(assessment.Rule,actual.GrammarRule);
        }
    }

}
