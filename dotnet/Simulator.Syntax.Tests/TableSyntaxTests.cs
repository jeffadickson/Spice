using FsCheck;
using FsCheck.Xunit;
using Xunit;
namespace Simulator.Syntax.Tests;

public sealed class TableSyntaxTests
{
    [Theory]
    [InlineData("G1 out 0 TABLE {V(VC+,VC-)} = (0,0)\n+(2,1m)")]
    [InlineData("E1 out 0 table {V(in)} (-1,-2) (1,2)")]
    [InlineData("G1 (out,0) TaBlE {(V(VC+,VC-))}=(-1,-1n)(0,0)(3.4k,1n)")]
    [InlineData("E1 out 0 TABLE {max(V(a+,a-),0)} = (0 0) (2 4)")]
    public void Controlled_tables_are_syntax_without_simulation_support(string card)
    {
        var text="TABLE profile\n"+card+"\n.end\n";
        var result=DeckParser.Parse(text,collectFeatures:true,collectDeclarations:true);
        Assert.Equal("accepted_structure",result.Status);
        Assert.Contains(result.Features,f=>f.Category=="device"&&f.Name==card[..1].ToLowerInvariant());
        Assert.Contains(result.Features,f=>f.Category=="source_form"&&f.Name=="table");
    }
    [Theory]
    [InlineData("((0,0)(4,.008))")]
    [InlineData("((0,0)(4,.008)")]
    [InlineData("((0,0)(1,.002))(4,.008))")]
    [InlineData("0,0,4,.008")]
    [InlineData("((-1,-1n)(0,0)(3.4k,1n))(3.5k,1))")]
    [InlineData("((-3.5k,-1)((-3.4k,-1n)(0,0)(1,1n))")]
    public void Native_table_coordinate_parentheses_are_separators_not_expression_groups(string coordinates)
        =>Assert.Equal("accepted_structure",DeckParser.Parse("Native coordinate profile\nG1 out 0 TABLE {V(in)} "+coordinates+"\n.end\n").Status);
    [Theory]
    [InlineData("TABLE {V(in)} =")]
    [InlineData("TABLE {V(in)} = (0) (1,2)")]
    [InlineData("TABLE {V(in)} = (0,1,2)")]
    [InlineData("TABLE {V(in)+} = (0,0) (1,2)")]
    [InlineData("TABLE {V(in)} == (0,0) (1,2)")]
    public void Table_context_keeps_expression_and_coordinate_arity_strict(string tail)
    {
        var parsed=DeckParser.Parse("Malformed TABLE\nG1 out 0 "+tail+"\n.end\n",collectDeclarations:true);
        Assert.Equal("syntax_rejected",parsed.Status);Assert.Null(parsed.Declarations);
    }
    [Theory]
    [InlineData("table a 0 b 0 Z0=50 TD=1n")]
    [InlineData("E1 out 0 table 0 2")]
    public void Table_spelling_is_not_a_globally_reserved_device_or_node_name(string card)
        =>Assert.Equal("accepted_structure",DeckParser.Parse("Name collision\n"+card+"\n.end\n").Status);
    [Fact]
    public void Table_remains_an_ordinary_identifier_outside_controlled_tail()
    {
        var result=DeckParser.Parse("Identifier\n.param table=3 p={table+2}\nR1 table 0 1k\n.end\n",collectDeclarations:true);
        Assert.Equal("accepted_structure",result.Status);
        Assert.Equal(5,ParameterEvaluator.Evaluate(result.Declarations!).Values["p"]);
    }
    [Property(MaxTest=128,Replay="906,20261101")]
    public bool Generated_table_prediction_and_physical_ranges_agree(PositiveInt count)
    {
        var n=2+count.Get%24;
        var text="Property\nG1 out 0 TABLE {V(in+,in-)} = "+string.Join("\n+",Enumerable.Range(0,n).Select(i=>$"({i},{i*2})"))+"\n.end\n";
        var fast=DeckParser.Parse(text,collectFeatures:true);
        var full=DeckParser.Parse(text,new(useSllFirst:false),collectFeatures:true);
        return fast.Status=="accepted_structure"&&fast.Status==full.Status&&fast.Features.SequenceEqual(full.Features)
            &&fast.Features.Count(f=>f.Category=="source_form"&&f.Name=="table")==1
            &&fast.Features.Where(f=>f.Category=="source_form"&&f.Name=="table").All(f=>
                f.Range.Start==text.IndexOf("TABLE",StringComparison.Ordinal)
                &&text.Substring(f.Range.Start,f.Range.Length)==text[text.IndexOf("TABLE",StringComparison.Ordinal)..text.IndexOf("\n.end",StringComparison.Ordinal)]);
    }
}
