using System.Text.Json;
using Simulator.Client;
using Simulator.Syntax;
using Xunit;

namespace Simulator.Frontend.Tests;

public sealed class RequestedPrefixFunctionTests
{
    [Fact]
    public void A_reporting_function_defined_before_op_is_available_to_later_conditions()
    {
        var syntax=DeckParser.Parse("Prefix function\nV1 in 0 5\nR1 in 0 1k\n.control\ndefine mismatch(a,b,err) abs(a-b)>err\nop\nlet foo = 2--3\nif mismatch(v(in),5,1e-9)\nquit 1\nend\necho foo=$&v(foo)\nquit 0\n.endc\n.end\n",collectDeclarations:true);
        var plan=RequestedAnalysis.Prepare(syntax);
        Assert.True(plan.Success,string.Join(',',plan.Diagnostics.Select(d=>d.Code)));
        var operation=Assert.Single(plan.Operations);
        var circuit=plan.CircuitPreparation!;
        var request=operation.Requests.Single();
        var source=Assert.Single(request.Circuit.Elements.OfType<Voltage>());
        var manifest=new ResultManifest("1",request.CaseId,"succeeded",true,
            [new("v("+circuit.RootNodes["in"]+")","voltage","V"),new("i("+source.Id+")","current","A")],
            [new(0,[5,-.005])],[],[],[],null,
            new("nalgebra_dense_lu_reference","cpu",128,false,false,true,false,"test"),"hash");
        var response=new SimulationResponse("1","succeeded",true,JsonSerializer.SerializeToElement(manifest,WireFormat.Options));
        var report=RequestedAnalysis.ExecuteTerminalReporting(syntax,plan,[response]);
        Assert.Equal(0,report.ExitCode);
        Assert.Empty(report.Diagnostics);
        Assert.Equal("foo=5",Assert.Single(report.Output));
    }
}
