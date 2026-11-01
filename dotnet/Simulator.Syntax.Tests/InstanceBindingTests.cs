using Xunit;
using FsCheck;
using FsCheck.Xunit;
namespace Simulator.Syntax.Tests;

public sealed class InstanceBindingTests
{
    private static DeclarationCatalog Catalog(string body) => DeckParser.Parse("Binding fixture\n"+body+"\n.end\n",collectDeclarations:true).Declarations!;

    [Fact]
    public void X_ports_bind_in_order_to_forward_numeric_case_insensitive_definitions()
    {
        var text="X1 out 0 12 gain=3\n.subckt 12 P N params: gain=2\nR1 P N 1k\n.ends 12";
        var catalog=Catalog(text);var result=InstanceBinder.Bind(catalog);
        var call=Assert.Single(result.Instances);
        Assert.Equal(BindingStatus.Bound,call.Status);
        Assert.Equal(new[]{"P","N"},call.Ports.Select(p=>p.Formal!.Text));
        Assert.Equal(new[]{"out","0"},call.Ports.Select(p=>p.Actual.Text));
        Assert.Equal("12",call.Target!.Name);
        Assert.Equal("gain",Assert.Single(call.Header.Parameters).Name);
        Assert.Equal(2,call.Header.Range.Line);
    }

    [Theory]
    [InlineData("X1 a cell\n.subckt cell p n\n.ends",BindingStatus.Invalid,"port_arity")]
    [InlineData("X1 a b absent",BindingStatus.Missing,"missing_reference")]
    [InlineData("X1 a b cell\n.subckt cell p n\n.ends\n.subckt CELL p n\n.ends",BindingStatus.Ambiguous,"ambiguous_reference")]
    [InlineData("X1 a b cell\n.subckt cell p P\n.ends",BindingStatus.Invalid,"duplicate_formal_port")]
    [InlineData(".include 'not-opened.lib'\nX1 a b missing",BindingStatus.Deferred,"provisional_reference")]
    public void Binding_preserves_explicit_failure_or_uncertainty(string text,BindingStatus status,string code)
    {
        var result=InstanceBinder.Bind(Catalog(text));
        var call=Assert.Single(result.Instances);
        Assert.Equal(status,call.Status);Assert.Equal(code,call.Code);Assert.False(result.Complete);
    }

    [Fact]
    public void Model_aware_binding_recovers_optional_bjt_substrate_without_trusting_parser_partition()
    {
        var report=InstanceBinder.Bind(Catalog("Q1 c b e substrate mod area=2\n.model mod npn\nD1 a 0 dm\n.model dm d"));
        Assert.All(report.Instances,i=>Assert.Equal(BindingStatus.Bound,i.Status));
        Assert.Equal(new[]{"c","b","e","substrate"},report.Instances[0].Ports.Select(p=>p.Actual.Text));
        Assert.Equal("mod",report.Instances[0].Target!.Name);
        Assert.Equal("dm",report.Instances[1].Target!.Name);
    }

    [Fact]
    public void Nested_lookup_shadows_ancestors_and_recursion_is_reported_separately()
    {
        var report=InstanceBinder.Bind(Catalog(".subckt outer p n\nX1 p n cell\n.subckt cell a b\nX2 a b outer\n.ends\n.ends\n.subckt cell q r\n.ends"));
        Assert.Equal(1,report.Instances[0].Target!.ScopeId);
        Assert.Contains(report.Diagnostics,d=>d.Code=="recursive_reference");Assert.False(report.Complete);
    }

    [Fact]
    public void Duplicate_instance_ids_do_not_silently_overwrite_and_opaque_headers_are_deferred()
    {
        var report=InstanceBinder.Bind(Catalog("D1 a 0 m\nd1 b 0 m\n.model m d"));
        Assert.Contains(report.Diagnostics,d=>d.Code=="duplicate_instance");Assert.False(report.Complete);
    }

    [Theory]
    [InlineData(1)] [InlineData(2)] [InlineData(7)] [InlineData(20)]
    public void Ordered_port_maps_are_invariant_under_systematic_node_renaming(int count)
    {
        var ports=string.Join(' ',Enumerable.Range(0,count).Select(i=>$"p{i}"));
        var nodes=string.Join(' ',Enumerable.Range(0,count).Select(i=>$"wire{i}"));
        var result=InstanceBinder.Bind(Catalog($"X1 {nodes} cell\n.subckt cell {ports}\n.ends"));
        Assert.Equal(Enumerable.Range(0,count).Select(i=>$"wire{i}"),Assert.Single(result.Instances).Ports.Select(p=>p.Actual.Text));
    }
    [Property(MaxTest=128,Replay="801,20261101")]
    public bool Renamed_positional_connections_preserve_order(PositiveInt value)
    {
        var count=1+value.Get%24;
        var ports=Enumerable.Range(0,count).Select(i=>$"p{i}").ToArray();
        var actual=Enumerable.Range(0,count).Select(i=>$"net{i}").ToArray();
        var report=InstanceBinder.Bind(Catalog($"X1 {string.Join(' ',actual)} CeLl\n.subckt cell {string.Join(' ',ports)}\n.ends"));
        return report.Complete&&report.Instances.Single().Ports.Select(p=>p.Actual.Text).SequenceEqual(actual);
    }    [Theory]
    [InlineData(128,true)] [InlineData(129,false)]
    public void Reference_graph_depth_has_an_inclusive_explicit_budget(int count,bool expected)
    {
        var text="Xroot a b cell0\n"+string.Join('\n',Enumerable.Range(0,count).Select(i=>$".subckt cell{i} p n\n"+(i+1<count?$"Xnext p n cell{i+1}\n":"")+".ends"));
        var report=InstanceBinder.Bind(Catalog(text));Assert.Equal(expected,report.Complete);
        if(!expected)Assert.Contains(report.Diagnostics,d=>d.Code=="binding_depth");
    }
    [Fact]
    public void Switch_control_reference_and_model_terminal_ordinals_are_retained()
    {
        var report=InstanceBinder.Bind(Catalog("W1 a b V1 m\n.model m csw"));
        var call=Assert.Single(report.Instances);Assert.Equal(BindingStatus.Bound,call.Status);
        Assert.Equal(new[]{0,1},call.Ports.Select(p=>p.Ordinal));Assert.All(call.Ports,p=>Assert.Null(p.Formal));
        Assert.Equal("V1",call.Header.Positional[2].Text);
    }
    [Fact]
    public void Model_absence_ambiguity_and_opaque_X_headers_have_distinct_outcomes()
    {
        Assert.Equal(BindingStatus.Missing,Assert.Single(InstanceBinder.Bind(Catalog("D1 a b absent")).Instances).Status);
        Assert.Equal(BindingStatus.Ambiguous,Assert.Single(InstanceBinder.Bind(Catalog("D1 a b m\n.model m d\n.model M npn")).Instances).Status);
        Assert.Equal(BindingStatus.Unsupported,Assert.Single(InstanceBinder.Bind(Catalog("X1 a {dynamic} cell\n.subckt cell p n\n.ends")).Instances).Status);
    }}
