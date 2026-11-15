using Xunit;
namespace Simulator.Syntax.Tests;
/// <summary>Exercises scalar function boundaries independently of circuit preparation.</summary>
public sealed class ScalarFunctionEvaluationTests
{
    [Theory]
    [InlineData(10,true)]
    [InlineData(11,false)]
    public void Parameter_edges_share_the_aggregate_node_budget(int count,bool accepted)
    {
        var definitions=string.Join('\n',Enumerable.Range(0,count).Select(i=>{
            var level=Enumerable.Range(0,256).Select(j=>j==0?$"p{i+1}":"1").ToArray();
            while(level.Length>1)level=level.Chunk(2).Select(pair=>$"({pair[0]}+{pair[1]})").ToArray();
            return $".func f{i}(x) {{{level[0]}}}\n.param p{i}={{f{i}(2)}}";
        }));
        var result=ParameterEvaluator.Evaluate(DeckParser.Parse($"nodes\n{definitions}\n.param p{count}=2\n.end\n",collectDeclarations:true).Declarations!);
        Assert.Equal(accepted,result.Success);
        if(accepted)Assert.Equal(2+count*255,result.Values["p0"]);
        else Assert.Contains(result.Diagnostics,d=>d.Code=="expression_budget");
    }
    [Theory]
    [InlineData(32,true)]
    [InlineData(33,false)]
    public void Parameter_edges_cannot_reset_outstanding_function_depth(int count,bool accepted)
    {
        var definitions=string.Join('\n',Enumerable.Range(0,count).Select(i=>$".func f{i}(x) {{p{i+1}}}\n.param p{i}={{f{i}(2)}}"));
        var parsed=DeckParser.Parse($"alternating\n{definitions}\n.param p{count}=2\n.end\n",collectDeclarations:true);
        var result=ParameterEvaluator.Evaluate(parsed.Declarations!);
        Assert.Equal(accepted,result.Success);
        if(accepted)Assert.Equal(2,result.Values["p0"]);
        else Assert.Contains(result.Diagnostics,d=>d.Code=="function_depth");
    }
    [Theory]
    [InlineData(32,true)]
    [InlineData(33,false)]
    public void Definition_call_depth_is_bounded(int count,bool accepted)
    {
        var definitions=string.Join('\n',Enumerable.Range(0,count).Select(i=>$".func f{i}(x) {{{(i==count-1?"x":$"f{i+1}(x)")}}}"));
        var parsed=DeckParser.Parse($"depth\n{definitions}\n.param result={{f0(2)}}\n.end\n",collectDeclarations:true);
        var result=ParameterEvaluator.Evaluate(parsed.Declarations!);
        Assert.Equal(accepted,result.Success);
        if(accepted)Assert.Equal(2,result.Values["result"]);
        else{Assert.Empty(result.Values);Assert.Contains(result.Diagnostics,d=>d.Code=="function_depth");}
    }
    [Theory]
    [InlineData(".func sin(x) {2*x}","sin(2)",4)]
    [InlineData(".func f(x) {x}","f(3)",3)]
    [InlineData(".func f(x) {2*x+1}","f(3)",7)]
    [InlineData(".func g(y) {y+1}\n.func f(x) {g(2*x)}","f(3)",7)]
    public void Function_arguments_are_values_with_preserved_precedence(string definitions,string call,double expected)
    {
        var parsed=DeckParser.Parse($"functions\n.param x=999\n{definitions}\n.param result={{{call}}}\n.end\n",collectDeclarations:true);
        var result=ParameterEvaluator.Evaluate(parsed.Declarations!);
        Assert.True(result.Success,string.Join(',',result.Diagnostics.Select(d=>d.Code)));Assert.Equal(expected,result.Values["result"]);
    }
}
