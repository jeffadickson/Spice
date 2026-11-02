using FsCheck;
using FsCheck.Xunit;
using Xunit;
namespace Simulator.Syntax.Tests;

public sealed class VoltageReferenceTests
{
    [Theory]
    [InlineData("V(VC+,VC-)")]
    [InlineData("LIMIT(0.5*V(REF+,REF-),-1,1)")]
    [InlineData("IF(V(IN1+,IN1-)>0,1,0)")]
    [InlineData("sin(v ( node+ , node- ))+2")]
    [InlineData("V(2p,0)")]
    [InlineData("V(bus[3],ref:minus)")]
    [InlineData("V(path/a,path-b)")]
    [InlineData("V(\"node+\",\"node-\")")]
    [InlineData("V(n+\n+ ,n-)")]
    [InlineData("V\n+ (VC+,VC-)")]
    [InlineData("V\n* between tokens\n+ (VC+,VC-)")]
    public void Voltage_arguments_preserve_node_spelling_inside_expressions(string expression)
    {
        var text="References\n.param p={"+expression+"}\n.end\n";
        var parsed=DeckParser.Parse(text,collectFeatures:true,collectDeclarations:true);
        Assert.Equal("accepted_structure",parsed.Status);
        var call=Assert.Single(parsed.Features.Where(f=>f.Category=="call"&&f.Name=="v"));
        var physical=text.Substring(call.Range.Start,call.Range.Length);
        Assert.StartsWith("v",physical,StringComparison.OrdinalIgnoreCase);Assert.Contains(")",physical);
        Assert.Contains(expression,Assert.Single(parsed.Declarations!.Parameters).Syntax.Value.Text);
        var evaluated=ParameterEvaluator.Evaluate(parsed.Declarations);
        Assert.False(evaluated.Success);Assert.Empty(evaluated.Values);
        Assert.Contains(evaluated.Diagnostics,d=>d.Code=="unsupported_expression");
    }
    [Theory]
    [InlineData("V(a+,)")]
    [InlineData("V(,a-)")]
    [InlineData("V(a+,,b-)")]
    [InlineData("V(a+,b-")]
    [InlineData("V(a+,b-))+1")]
    [InlineData("V(a+,b-)+")]
    [InlineData("V(a(b))")]
    [InlineData("V()")]
    [InlineData("V(a,b,c)")]
    [InlineData("SIN(a+)")]
    [InlineData("vv(a+)")]
    [InlineData("1+")]
    [InlineData("VC-")]
    public void Reference_delimiters_and_ordinary_arithmetic_remain_strict(string expression)
    {
        var result=DeckParser.Parse("Bad reference\n.param p={"+expression+"}\n.end\n",collectDeclarations:true);
        Assert.Equal("syntax_rejected",result.Status);Assert.Null(result.Declarations);
    }
    [Theory]
    [InlineData("B1 out 0 V=V(VC+,VC-)")]
    [InlineData("G1 out 0 VALUE={LIMIT(GAIN*V(VC+,VC-),INEG,IPOS)}")]
    [InlineData(".func clip(g) {max(g*V(REF+,REF-),0)}")]
    public void Behavioral_and_function_contexts_use_the_same_generated_reference_grammar(string card)
        =>Assert.Equal("accepted_structure",DeckParser.Parse("Reference context\n"+card+"\n.end\n").Status);
    [Fact]
    public void A_variable_named_v_keeps_ordinary_arithmetic_meaning()
    {
        var parsed=DeckParser.Parse("Variable\n.param v=3 p={v+(2)}\n.end\n",collectDeclarations:true);
        Assert.Equal(5,ParameterEvaluator.Evaluate(parsed.Declarations!).Values["p"]);
    }
    [Theory]
    [InlineData(126,"accepted_structure")]
    [InlineData(127,"resource_rejected")]
    public void Reference_parenthesis_counts_toward_the_existing_group_budget(int outer,string expected)
    {
        var text="Groups\n.param p={"+new string('(',outer)+"V(n)"+new string(')',outer)+"}\n.end\n";
        Assert.Equal(expected,DeckParser.Parse(text).Status);
    }
    [Property(MaxTest=128,Replay="905,20261101")]
    public bool Punctuated_reference_prediction_and_source_ranges_agree(PositiveInt number)
    {
        var id=number.Get%10_000;
        var text=$"Property\n.param p={{max(V(n{id}+,n{id}-),0)}}\n.end\n";
        var fast=DeckParser.Parse(text,collectFeatures:true);
        var full=DeckParser.Parse(text,new(useSllFirst:false),collectFeatures:true);
        return fast.Status=="accepted_structure"&&full.Status==fast.Status
            &&fast.Features.SequenceEqual(full.Features)
            &&fast.Features.Count(f=>f.Category=="call"&&f.Name=="v")==1;
    }
    [Theory]
    [InlineData(126,"accepted_structure")]
    [InlineData(127,"resource_rejected")]
    public void Operands_between_nested_calls_cannot_refund_group_budget(int depth,string expected)
    {
        var expression=string.Concat(Enumerable.Repeat("max(1,",depth))+"V(n)"+new string(')',depth);
        var parsed=DeckParser.Parse("Nested calls\n.param p={"+expression+"}\n.end\n");
        Assert.Equal(expected,parsed.Status);
        if(expected=="resource_rejected")Assert.Contains(parsed.Diagnostics,d=>d.Message=="Nesting exceeds 128 groups");
    }
}
