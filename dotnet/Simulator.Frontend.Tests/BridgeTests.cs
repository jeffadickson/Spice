using Simulator.Syntax;
using Simulator.Client;
using Xunit;
using FsCheck;
using FsCheck.Xunit;
namespace Simulator.Frontend.Tests;
public sealed class BridgeTests
{
    private static BridgeResult Lower(string body,BridgeLimits? limits=null)=>OperatingPointBridge.Lower(DeckParser.Parse("Bridge\n"+body+"\n.end\n",collectDeclarations:true),limits);
    [Fact]
    public void Hierarchical_resistors_and_caller_overrides_become_typed_Rust_requests()
    {
        var result=Lower(".param r=1k\nV1 in 0 3\nRseries in out 1k\nX1 out 0 load gain=3\n.subckt load p n gain=2\nR1 p middle {r*gain}\nR2 middle n {r}\n.ends\n.op");
        Assert.True(result.Success,string.Join(',',result.Diagnostics.Select(d=>d.Code)));
        var request=result.Request!;Assert.IsType<OperatingPoint>(request.Analysis);
        Assert.Equal(new[]{1000.0,3000.0,1000.0},request.Circuit.Elements.OfType<Resistor>().Select(r=>r.Ohms));
        Assert.Equal(4,request.Circuit.Nodes.Length);Assert.Equal("0",request.Circuit.Nodes[0]);
        Assert.Equal(4,result.Origins.Length);Assert.Contains(request.Circuit.Nodes,n=>n==result.RootNodes["OUT"]);
    }
    [Theory]
    [InlineData(".end\nR1 a 0 1k","unprocessed_end_tail")]
    [InlineData("X1 a 0 cell\n.subckt cell p n\n.end\nR1 p n 1k\n.ends","unsupported_active_card")]
    [InlineData(".param dc=3\nV1 a 0 dc\nR1 a 0 1k","unsupported_primitive_header")]
    [InlineData("R1 a 0 dc 1k","unsupported_primitive_header")]
    [InlineData("C1 a 0 1u","unsupported_active_card")]
    [InlineData("V1 a 0 SIN(0 1 1k)","unsupported_primitive_header")]
    [InlineData("R1 a 0 {missing}","missing_parameter")]
    [InlineData("R1 a 0 -1","invalid_resistance")]
    [InlineData("R1 a 0 1k\nr1 b 0 2k","duplicate_element")]
    [InlineData(".tran 1n 1u\nR1 a 0 1k","unsupported_active_card")]
    [InlineData(".control\nop\n.endc\nR1 a 0 1k","unsupported_active_card")]
    [InlineData("X1 a 0 missing","missing_reference")]
    public void Unsupported_or_invalid_inputs_publish_no_partial_request(string body,string code)
    {
        var result=Lower(body);Assert.False(result.Success);Assert.Null(result.Request);Assert.Empty(result.Origins);Assert.Empty(result.RootNodes);Assert.Contains(result.Diagnostics,d=>d.Code==code);
    }
    [Fact]
    public void Private_nodes_are_distinct_and_unused_unsupported_bodies_are_not_executed()
    {
        var result=Lower("X1 a 0 cell\nX2 b 0 cell\n.subckt cell p n\nR1 p private 1k\nR2 private n 1k\n.ends\n.subckt unused p n\nB1 p n V={time}\n.ends");
        Assert.True(result.Success);Assert.Equal(5,result.Request!.Circuit.Nodes.Length);
        Assert.Equal(4,result.Request.Circuit.Elements.Select(e=>e.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }
    [Fact]
    public void Active_recursion_and_output_budgets_are_bounded()
    {
        Assert.False(Lower("X1 a 0 cell\n.subckt cell p n\nX2 p n cell\n.ends").Success);
        Assert.Contains(Lower("R1 a 0 1k\nR2 b 0 1k",new(MaxElements:1)).Diagnostics,d=>d.Code=="element_budget");
        Assert.Contains(Lower("R1 a 0 1k\nR2 b 0 1k",new(MaxNodes:2)).Diagnostics,d=>d.Code=="node_budget");
    }
    [Property(MaxTest=64,Replay="901,20261101")]
    public bool Separate_calls_never_alias_private_nodes(PositiveInt number)
    {
        var count=1+number.Get%12;var body=string.Join('\n',Enumerable.Range(0,count).Select(i=>$"X{i} a 0 cell"))+"\n.subckt cell p n\nR1 p private 1k\nR2 private n 1k\n.ends";
        var result=Lower(body);return result.Success&&result.Request!.Circuit.Nodes.Length==count+2&&result.Request.Circuit.Elements.Length==count*2;
    }
    [Theory]
    [InlineData(-1,128,1024,128)]
    [InlineData(129,128,1024,128)]
    [InlineData(32,0,1024,128)]
    [InlineData(32,128,0,128)]
    [InlineData(32,128,1025,128)]
    [InlineData(32,128,1024,0)]
    [InlineData(32,128,1024,129)]
    public void Invalid_budget_contract_is_rejected(int depth,int instances,int elements,int nodes)
        =>Assert.Throws<ArgumentOutOfRangeException>(()=>Lower("R1 a 0 1k",new(depth,instances,elements,nodes)));
    [Fact]
    public void Inclusive_limits_and_zero_resistance_have_explicit_boundaries()
    {
        Assert.True(Lower("R1 a 0 1k",new(MaxDepth:0,MaxInstances:1,MaxElements:1,MaxNodes:2)).Success);
        Assert.True(Lower("R1 a 0 1k",new(MaxDepth:128)).Success);
        Assert.Contains(Lower("R1 a 0 0").Diagnostics,d=>d.Code=="invalid_resistance");
        const string nested="X1 a 0 cell\n.subckt cell p n\nR1 p n 1k\n.ends";
        Assert.True(Lower(nested,new(MaxDepth:1,MaxInstances:2)).Success);
        Assert.Contains(Lower(nested,new(MaxDepth:0)).Diagnostics,d=>d.Code=="expansion_depth");
        Assert.Contains(Lower(nested,new(MaxInstances:1)).Diagnostics,d=>d.Code=="instance_budget");
    }
    [Theory]
    [InlineData("lu",true)]
    [InlineData("qr",true)]
    [InlineData("faer",true)]
    [InlineData("faer_sparse_lu",false)]
    public void Backend_uses_the_registered_wire_key(string backend,bool expected)
        =>Assert.Equal(expected,OperatingPointBridge.Lower(DeckParser.Parse("Bridge\nR1 a 0 1k\n.end\n",collectDeclarations:true),backend:backend).Success);
    [Theory]
    [InlineData("DC -2",-2)]
    [InlineData("DC +2",2)]
    [InlineData("-2",-2)]
    public void Signed_constant_source_preserves_the_sign(string value,double expected)
    {
        var result=Lower("V1 a 0 "+value+"\nR1 a 0 1k");
        Assert.True(result.Success,string.Join(',',result.Diagnostics.Select(d=>d.Code)));
        Assert.Equal(expected,Assert.IsType<Voltage>(result.Request!.Circuit.Elements[0]).Volts);
    }
    [Property(MaxTest=128,Replay="903,20261101")]
    public bool Arbitrary_bounded_text_never_publishes_a_partial_request(NonNull<string> input)
    {
        var result=Lower(input.Get[..Math.Min(input.Get.Length,128)]);
        if(!result.Success)return result.Request==null&&result.RootNodes.IsEmpty&&result.Origins.IsEmpty;
        var circuit=result.Request!.Circuit;
        return circuit.Elements.Length>0&&circuit.Nodes.Length<=128&&circuit.Elements.Length<=1024
            &&circuit.Elements.All(e=>e.P>=0&&e.P<circuit.Nodes.Length&&e.N>=0&&e.N<circuit.Nodes.Length&&e.P!=e.N);
    }
    [Fact]
    public void Public_result_requires_both_a_request_and_no_diagnostics()
    {
        var prepared=Lower("R1 a 0 1k");
        Assert.False((prepared with {Request=null}).Success);
        Assert.False((prepared with {Diagnostics=[new("refused",null)]}).Success);
        Assert.Contains(Lower("R1 a 0 1k",new(MaxNodes:1)).Diagnostics,d=>d.Code=="node_budget");
    }
}
