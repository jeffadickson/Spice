using System.Text.Json;
using Simulator.Client;
using Simulator.RequestedQualification;
using Simulator.Syntax;
using Xunit;

namespace Simulator.Frontend.Tests;

public sealed class RequestedUicTests
{
    [Theory]
    [InlineData("compat/benchmarks/rc-transient.cir", "trapezoidal")]
    [InlineData("compat/ngspice/qualification-rlc-decay.cir", "trapezoidal")]
    [InlineData("compat/ngspice/qualification-diode-charge-transient.cir", "backward_euler")]
    public void Original_decks_prepare_their_requested_method_and_uic_startup(string path, string method)
    {
        var root=Path.GetDirectoryName(SourceFile())!;
        var text=File.ReadAllText(Path.GetFullPath(Path.Combine(root,"../..",path)));
        var syntax=DeckParser.Parse(text,collectDeclarations:true);
        var plan=RequestedAnalysis.Prepare(syntax);
        Assert.True(plan.Success,string.Join(',',plan.Diagnostics.Select(d=>d.Code)));
        var operation=Assert.Single(plan.Operations);
        Assert.Equal(method,Assert.IsType<TransientConfigured>(Assert.Single(operation.Requests).Analysis).Options.Method);
        Assert.NotNull(operation.InitializationRequest);
        var projection=Simulator.RequestedQualification.OriginalProjection.Create(text,syntax,operation,plan);
        Assert.Contains("uic",projection.Text,StringComparison.OrdinalIgnoreCase);
        if(path.EndsWith("/rc-transient.cir",StringComparison.Ordinal))Assert.Contains("method=trap",projection.Text,StringComparison.OrdinalIgnoreCase);
    }

    [RequestedNativeFact]
    public async Task Startup_state_preserves_storage_and_solves_unclamped_algebraic_nodes()
    {
        const string deck="RLC startup\nR1 out mid 2\nL1 mid 0 4 IC=0\nC1 out 0 3\n.ic v(out)=1 v(mid)=99\n.tran .01 .1 uic\n.end\n";
        var plan=RequestedAnalysis.Prepare(DeckParser.Parse(deck,collectDeclarations:true));
        Assert.True(plan.Success,string.Join(',',plan.Diagnostics.Select(d=>d.Code)));
        var op=Assert.Single(plan.Operations);
        var result=await RequestedAnalysis.PrepareExecutionAsync(op,0,new(Environment.GetEnvironmentVariable("SPICE_CORPUS_ENGINE")!));
        Assert.True(result.Success,result.Failure);
        var startup=Assert.IsType<TransientConfigured>(result.Request!.Analysis).Options.InitialState!;
        var circuit=plan.CircuitPreparation!;
        double ReadNode(string name)=>startup[Array.IndexOf(result.Request.Circuit.Nodes,circuit.RootNodes[name])-1];
        Assert.Equal(1,ReadNode("out"),12);
        Assert.Equal(1,ReadNode("mid"),12);
        Assert.Equal(0,startup[^1]);
        Assert.DoesNotContain(op.InitialConditions,c=>c.OriginalNode=="mid");
    }

    [Theory]
    [InlineData("L1 out 0 1m IC=1\nC1 out 0 1u", "", "unsupported_primitive_header")]
    [InlineData("R1 in 0 1k\nC1 in out 1u\nR2 out 0 1k", "", "unsupported_uic_device_profile")]
    [InlineData("R1 out 0 1k\nC1 out 0 1u", ".options method=trap gmin=1e-8", "unsupported_physical_directive")]
    public void Unqualified_device_state_and_mixed_options_refuse_atomically(string elements,string options,string code)
    {
        var deck="Unsupported startup\n"+elements+"\n"+options+"\n.tran 1u 10u uic\n.end\n";
        var plan=RequestedAnalysis.Prepare(DeckParser.Parse(deck,collectDeclarations:true));
        Assert.Empty(plan.Operations);
        Assert.Contains(plan.Diagnostics,d=>d.Code==code);
    }

    [Theory]
    [InlineData(true, .05, true)]
    [InlineData(true, .15, false)]
    [InlineData(false, .05, false)]
    public void Only_the_uic_zero_sample_can_lack_a_native_counterpart(bool uic,double nativeStart,bool agrees)
    {
        var deck="Native start\nR1 out 0 1k\nC1 out 0 1u\n.tran .1 .2"+(uic?" uic":"")+"\n.end\n";
        var plan=RequestedAnalysis.Prepare(DeckParser.Parse(deck,collectDeclarations:true));
        Assert.True(plan.Success);
        var circuit=plan.CircuitPreparation!;
        var op=Assert.Single(plan.Operations);
        var manifest=new ResultManifest("1",op.Requests[0].CaseId,"succeeded",true,
            [new("v("+circuit.RootNodes["out"]+")","voltage","V")],
            [new(0,[0]),new(.1,[0]),new(.2,[0])],[],[],[],null,
            new("nalgebra_dense_lu_reference","cpu",128,false,false,true,false,"test"),"hash");
        var response=new SimulationResponse("1","succeeded",true,JsonSerializer.SerializeToElement(manifest,WireFormat.Options));
        var native=new NativeRaw(false,[new("time","time"),new("v(out)","voltage")],
            [new[]{new ComplexValue(nativeStart,0),new ComplexValue(0,0)},new[]{new ComplexValue(.2,0),new ComplexValue(0,0)}])
            {PlotName="Transient Analysis"};
        var comparison=RequestedComparison.Compare(op,circuit,[response],native);
        Assert.Equal(agrees,comparison.Agreed);
        if(agrees){Assert.Equal(1,comparison.ExcludedStartupSamples);Assert.Equal(2,Assert.Single(comparison.Observables).Points);}
    }

    [FsCheck.Xunit.Property(MaxTest=100,Replay="731,20261107")]
    public bool Root_hints_only_constrain_the_grounded_storage_node(FsCheck.NonNegativeInt input)
    {
        var volts=(input.Get%2001)-1000;
        var literal=volts.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var deck="Hints\nR1 out mid 2\nR2 mid 0 1k\nC1 out 0 1u\n.ic v(out)="+literal+" v(mid)=999\n.tran .01 .1 uic\n.end\n";
        var plan=RequestedAnalysis.Prepare(DeckParser.Parse(deck,collectDeclarations:true));
        if(!plan.Success)return false;
        var operation=plan.Operations.Single();
        return operation.InitialConditions is [{OriginalNode:"out"} condition]&&condition.Volts==volts
            &&operation.InitializationRequest!.Circuit.Elements.OfType<Voltage>().Single().Volts==volts;
    }

    private static string SourceFile([System.Runtime.CompilerServices.CallerFilePath]string path="")=>path;
}
