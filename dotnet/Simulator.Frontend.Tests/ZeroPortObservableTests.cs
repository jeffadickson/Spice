using System.Collections.Immutable;
using System.Text.Json;
using Simulator.Client;
using Simulator.RequestedQualification;
using Simulator.Syntax;
using Xunit;

namespace Simulator.Frontend.Tests;

public sealed class ZeroPortObservableTests
{
    [Theory]
    [InlineData(1,-.001,true)]
    [InlineData(2,-.001,false)]
    [InlineData(1,-.002,false)]
    public void Internal_physical_voltage_and_branch_are_compared_without_root_ports(double voltage,double current,bool agrees)
    {
        var syntax=DeckParser.Parse("Zero\nX1 zero\n.subckt zero\nR1 hidden 0 1k\nV1 hidden 0 1\n.ends\n.op\n.end\n",collectDeclarations:true);
        var plan=RequestedAnalysis.Prepare(syntax);
        Assert.True(plan.Success);
        var circuit=plan.CircuitPreparation!;
        Assert.Empty(circuit.RootNodes);
        var operation=Assert.Single(plan.Operations);
        var node=circuit.Request!.Circuit.Nodes[1];
        var source=Assert.Single(circuit.Request.Circuit.Elements.OfType<Voltage>());
        var manifest=new ResultManifest("1",operation.Requests[0].CaseId,"succeeded",true,
            [new("v("+node+")","voltage","V"),new("i("+source.Id+")","current","A")],
            [new(0,[voltage,current])],[],[],[],null,
            new("nalgebra_dense_lu_reference","cpu",128,false,false,true,false,"test"),"hash");
        var response=new SimulationResponse("1","succeeded",true,JsonSerializer.SerializeToElement(manifest,WireFormat.Options));
        var raw=new NativeRaw(false,[new("v(x1.hidden)","voltage"),new("i(v.x1.v1)","current")],
            [new[]{new ComplexValue(1,0),new ComplexValue(-.001,0)}]){PlotName="Operating Point"};
        var result=RequestedComparison.Compare(operation,circuit,[response],raw);
        Assert.Equal(agrees,result.Agreed);
        Assert.Equal(2,result.Observables.Length);
    }
    [Fact]
    public void Rendered_native_instance_name_collisions_are_refused()
    {
        var syntax=DeckParser.Parse("""
Collision
X1.x2 leaf
X1 parent
.subckt parent
X2 leaf
.ends
.subckt leaf
V1 hidden 0 1
R1 hidden 0 1k
.ends
.op
.end

""",collectDeclarations:true);
        var plan=RequestedAnalysis.Prepare(syntax);
        Assert.False(plan.Success);
        Assert.Contains(plan.Diagnostics,d=>d.Code=="ambiguous_native_internal_node");
    }

}
