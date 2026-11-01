using FsCheck;
using FsCheck.Xunit;
using Xunit;
namespace Simulator.Syntax.Tests;
public sealed class ParameterEvaluationTests
{
    private static DeclarationCatalog Catalog(string body)=>DeckParser.Parse("Parameters\n"+body+"\n.end\n",collectDeclarations:true).Declarations!;
    [Theory]
    [InlineData("1M",0.001)] [InlineData("1MEG",1000000.0)]
    [InlineData("2kOhm",2000.0)] [InlineData("1mil",0.0000254)]
    [InlineData("{2+3*4}",14.0)] [InlineData("{(2+3)*4}",20.0)]
    [InlineData("{2^3}",8.0)] [InlineData("{2^3^2}",64.0)]
    [InlineData("{-2^2}",-4.0)] [InlineData("{(-2)^2}",4.0)] [InlineData("'3*4'",12.0)]
    [InlineData("{sqrt(9)+abs(-2)}",5.0)]
    [InlineData("{1||0&&0}",1.0)] [InlineData("{0&&1||1}",1.0)]
    [InlineData("{1?0:1?2:3}",0.0)] [InlineData("{pow(-2,3)}",-8.0)]
    [InlineData("{!0}",1.0)] [InlineData("{1?7:9}",7.0)] [InlineData("{0?7:9}",9.0)]
    [InlineData("{5%2}",1.0)] [InlineData("{1<=1}",1.0)] [InlineData("{2!=2}",0.0)]
    [InlineData("{min(2,3)+max(4,5)}",7.0)] [InlineData("{exp(0)+ln(1)+sin(0)+cos(0)}",2.0)]
    public void Numeric_semantics_have_independent_expected_values(string expression,double expected)
    {
        var result=ParameterEvaluator.Evaluate(Catalog(".param result="+expression));
        Assert.True(result.Success,string.Join(',',result.Diagnostics.Select(d=>d.Code)));
        Assert.Equal(expected,result.Values["RESULT"],12);
    }
    [Fact]
    public void Dependencies_shadowing_repeated_assignments_and_forward_references_are_explicit()
    {
        var catalog=Catalog(".param a=1 a=2 root={a*3}\n.subckt cell p n gain={root+1}\n.param a=4 local={gain+a}\n.ends");
        var root=ParameterEvaluator.Evaluate(catalog);
        Assert.Equal(2,root.Values["a"]);Assert.Equal(6,root.Values["root"]);
        var local=ParameterEvaluator.Evaluate(catalog,1);
        Assert.Equal(4,local.Values["a"]);Assert.Equal(6,local.Values["root"]);Assert.Equal(11,local.Values["local"]);
        var forward=ParameterEvaluator.Evaluate(Catalog(".param a={b+1} b=2"));Assert.Equal(3,forward.Values["a"]);
    }
    [Fact]
    public void Call_overrides_evaluate_in_caller_and_recompute_dependent_defaults_in_callee()
    {
        var catalog=Catalog(".param base=3\nX1 a b cell gain={base*2}\n.subckt cell p n gain=2 derived={gain*4}\n.ends");
        var call=Assert.Single(InstanceBinder.Bind(catalog).Instances);
        var result=ParameterEvaluator.ForInstance(catalog,call,ParameterEvaluator.Evaluate(catalog));
        Assert.True(result.Success);Assert.Equal(6,result.Values["gain"]);Assert.Equal(24,result.Values["derived"]);
    }
    [Theory]
    [InlineData("a={b} b={a}","parameter_cycle")]
    [InlineData("a={missing+1}","missing_parameter")]
    [InlineData("a={1/0}","nonfinite_parameter")]
    [InlineData("a={sqrt(-1)}","nonfinite_parameter")]
    [InlineData("a={mystery(1)}","unsupported_expression")]
    [InlineData("f(x)=1","opaque_parameter_target")]
    public void Failure_is_source_located_atomic_and_preserves_catalog(string assignments,string code)
    {
        var catalog=Catalog(".param valid=42 "+assignments);var before=catalog.Parameters;
        var result=ParameterEvaluator.Evaluate(catalog);
        Assert.False(result.Success);Assert.Empty(result.Values);Assert.Contains(result.Diagnostics,d=>d.Code==code);
        Assert.Equal(before,catalog.Parameters);Assert.All(result.Diagnostics,d=>Assert.True(d.Range.Line>=2));
    }
    [Fact]
    public void Unresolved_resources_and_dependency_depth_remain_explicit()
    {
        Assert.Equal("deferred_parameters",Assert.Single(ParameterEvaluator.Evaluate(Catalog(".include 'not-opened'\n.param a=2")).Diagnostics).Code);
        var text=".param p0=1 "+string.Join(' ',Enumerable.Range(1,70).Select(i=>$"p{i}={{p{i-1}+1}}"));
        // Reverse declaration order requires DFS rather than relying on enumeration order.
        var reversed=".param "+string.Join(' ',Enumerable.Range(1,70).Reverse().Select(i=>$"p{i}={{p{i-1}+1}}"))+" p0=1";
        Assert.False(ParameterEvaluator.Evaluate(Catalog(reversed)).Success);
    }
    [Property(MaxTest=128,Replay="802,20261101")]
    public bool Generated_acyclic_chains_match_closed_form(PositiveInt value)
    {
        var count=1+value.Get%24;
        var text=".param p0=2 "+string.Join(' ',Enumerable.Range(1,count).Select(i=>$"p{i}={{p{i-1}+3}}"));
        var result=ParameterEvaluator.Evaluate(Catalog(text));
        return result.Success&&result.Values[$"p{count}"]==2+3*count;
    }
    [Fact]
    public void Nested_calls_retain_the_evaluated_enclosing_instance_environment()
    {
        var catalog=Catalog("Xroot a b outer gain=6\n.subckt outer p n gain=2\nXinner p n inner\n.subckt inner p n derived={gain*4}\n.ends\n.ends");
        var calls=InstanceBinder.Bind(catalog).Instances;
        var outer=ParameterEvaluator.ForInstance(catalog,calls[0],ParameterEvaluator.Evaluate(catalog));
        var inner=ParameterEvaluator.ForInstance(catalog,calls[1],outer);
        Assert.True(inner.Success);Assert.Equal(24,inner.Values["derived"]);
        var wrong=ParameterEvaluator.ForInstance(catalog,calls[1],ParameterEvaluator.Evaluate(catalog));
        Assert.False(wrong.Success);Assert.Empty(wrong.Values);
    }
    [Fact]
    public void Dependency_failures_point_to_the_failing_assignment_not_the_initial_request()
    {
        var catalog=Catalog(".param a={b} b={missing}");
        var result=ParameterEvaluator.Evaluate(catalog);
        Assert.Equal(catalog.Parameters[1].Syntax.Range,Assert.Single(result.Diagnostics).Range);
    }    [Fact]
    public void Assignment_and_override_work_are_bounded_before_evaluation()
    {
        var assignments=".param "+string.Join(' ',Enumerable.Range(0,4097).Select(i=>$"p{i}=1"));
        Assert.Equal("parameter_budget",Assert.Single(ParameterEvaluator.Evaluate(Catalog(assignments)).Diagnostics).Code);
        var overrides=string.Join(' ',Enumerable.Repeat("gain=1",4097));
        var catalog=Catalog($"X1 a b cell {overrides}\n.subckt cell p n gain=2\n.ends");
        var call=Assert.Single(InstanceBinder.Bind(catalog).Instances);
        var result=ParameterEvaluator.ForInstance(catalog,call,ParameterEvaluator.Evaluate(catalog));
        Assert.False(result.Success);Assert.Equal("parameter_budget",Assert.Single(result.Diagnostics).Code);
    }    [Property(MaxTest=128,Replay="803,20261101")]
    public bool Separate_instances_keep_independent_nested_environments(PositiveInt number)
    {
        var gain=1+number.Get%100;
        var catalog=Catalog($"Xfirst a b outer gain={gain}\nXsecond c d outer gain={gain+1}\n.subckt outer p n gain=2\nXinner p n inner\n.subckt inner p n derived={{gain*4}}\n.ends\n.ends");
        var calls=InstanceBinder.Bind(catalog).Instances;var root=ParameterEvaluator.Evaluate(catalog);
        var first=ParameterEvaluator.ForInstance(catalog,calls[0],root);var second=ParameterEvaluator.ForInstance(catalog,calls[1],root);
        var a=ParameterEvaluator.ForInstance(catalog,calls[2],first);var b=ParameterEvaluator.ForInstance(catalog,calls[2],second);
        return a.Success&&b.Success&&a.Values["derived"]==gain*4&&b.Values["derived"]==(gain+1)*4;
    }    [Fact]
    public void A_foreign_catalog_binding_cannot_be_combined_with_a_local_caller()
    {
        var a=Catalog("X1 a b cell gain=3\n.subckt cell p n gain=2\n.ends");
        var b=Catalog("X1 a b cell gain=7\n.subckt cell p n gain=5\n.ends");
        var foreign=InstanceBinder.Bind(b).Instances.Single();
        var result=ParameterEvaluator.ForInstance(a,foreign,ParameterEvaluator.Evaluate(a));
        Assert.False(result.Success);Assert.Empty(result.Values);
        Assert.All(typeof(BoundInstance).GetProperties(),p=>Assert.Null(p.SetMethod));
    }}
