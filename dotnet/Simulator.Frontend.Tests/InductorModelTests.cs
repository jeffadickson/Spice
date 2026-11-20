using Simulator.Client;
using Simulator.Syntax;
using Simulator.RequestedQualification;
using System.Security.Cryptography;
using System.Text.Json;
using Xunit;

namespace Simulator.Frontend.Tests;

/// <summary>Constant flux coefficient preparation, independently checked against the native temperature law.</summary>
public sealed class InductorModelTests
{
    private static RequestedAnalysisPlan Prepare(string instance,string model="")=>RequestedAnalysis.Prepare(
        DeckParser.Parse("Inductance\nV1 a 0 AC 1\nR1 a b 1k\n"+instance+"\n"+model+"\n.op\n.ac lin 2 10 20\n.tran 1u 10u\n.end\n",collectDeclarations:true));

    [Theory]
    [InlineData("L1 b 0 L=20m M=2", "", .01)]
    [InlineData("L1 b 0 20m M=2 SCALE=3", "", .03)]
    [InlineData("L1 b 0 coil", ".model coil L(IND=20m)", .02)]
    [InlineData("L1 b 0 coil 10m TEMP=90 SCALE=2 M=4", ".model coil L(IND=20m TC1=.01 TC2=.926e-4)", .009987647)]
    [InlineData("L1 b 0 coil 10m TEMP=37 DTEMP=500 TC1=.02", ".model coil L(TC1=.01 TNOM=27)", .012)]
    [InlineData("L1 b 0 coil 10m DTEMP=10", ".model coil L(TC1=.01)", .011)]
    [InlineData("L1 b 0 coil 10m", ".model coil L(L=100)", .01)]
    public void Every_analysis_uses_the_resolved_positive_henry_value(string instance,string model,double henries)
    {
        var plan=Prepare(instance,model);
        Assert.True(plan.Success,string.Join(',',plan.Diagnostics.Select(d=>d.Code)));
        Assert.Equal(3,plan.Operations.Length);
        foreach(var request in plan.Operations.SelectMany(o=>o.Requests))
            Assert.Equal(henries,Assert.Single(request.Circuit.Elements.OfType<Inductor>()).Henries,12);
    }

    [Theory]
    [InlineData("L1 b 0 L=10m M=0", "")]
    [InlineData("L1 b 0 10m M=2 m=3", "")]
    [InlineData("L1 b 0 10m L=20m", "")]
    [InlineData("L1 b 0 10m TEMP=-274", "")]
    [InlineData("L1 b 0 coil 10m TEMP=37", ".model coil L(TC1=-.1)")]
    [InlineData("L1 b 0 coil 10m", ".model coil L(IND=20m UNKNOWN=2)")]
    [InlineData("L1 b 0 10m UNKNOWN=1", "")]
    [InlineData("L1 b 0 missing 10m", "")]
    [InlineData("L1 b 0 10m SCALE=1e308 M=1e-308", "")]
    [InlineData("L1 b 0 coil -10m TEMP=37", ".model coil L(TC1=-.2)")]
    [InlineData("L1 b 0 coil 10m TEMP=37 SCALE=-1", ".model coil L(TC1=-.2)")]
    [InlineData("L1 b 0 coil 10m TEMP=37 M=-1", ".model coil L(TC1=-.2)")]
    public void Unsupported_or_nonphysical_tails_refuse_the_whole_plan(string instance,string model)
    {
        var plan=Prepare(instance,model);Assert.False(plan.Success);Assert.Empty(plan.Operations);
        Assert.NotEmpty(plan.Diagnostics);
        Assert.All(plan.Diagnostics,d=>Assert.False(string.IsNullOrWhiteSpace(d.Code)));
    }

    [Fact]
    public void Multiplicity_does_not_multiply_the_authored_initial_current()
    {
        var syntax=DeckParser.Parse("UIC\nR1 a 0 1\nL1 a 0 L=20m M=2 IC=.75\n.tran 1u 10u uic\n.end\n",collectDeclarations:true);
        var plan=RequestedAnalysis.Prepare(syntax);
        Assert.True(plan.Success,string.Join(',',plan.Diagnostics.Select(d=>d.Code)));
        var operation=Assert.Single(plan.Operations);
        Assert.Equal(.75,operation.InductorInitialCurrents.Single().Value);
        Assert.Equal(.01,Assert.Single(operation.Requests[0].Circuit.Elements.OfType<Inductor>()).Henries,12);
    }

    [Fact]
    public void Model_parameters_use_their_lexical_frame_and_instance_overrides_remain_local()
    {
        var syntax=DeckParser.Parse("Scopes\n.param nominal=20m\nV1 a 0 AC 1\nX1 a 0 cell mult=2\nX2 a 0 cell mult=4\n"
            +".subckt cell p n params: mult=1\nR1 p x 1k\nL1 x n coil M={mult}\n.model coil L(IND={nominal})\n.ends\n.ac lin 2 10 20\n.end\n",collectDeclarations:true);
        var plan=RequestedAnalysis.Prepare(syntax);Assert.True(plan.Success,string.Join(',',plan.Diagnostics.Select(d=>d.Code)));
        Assert.Equal(new[]{.005,.01},plan.Operations[0].Requests[0].Circuit.Elements.OfType<Inductor>().Select(l=>l.Henries).Order());
        Assert.False(OperatingPointBridge.Lower(syntax).Success);
    }

    [FsCheck.Xunit.Property(MaxTest=64,Replay="2191,20261119")]
    public bool Parallel_winding_multiplicity_preserves_nominal_flux_scaling(FsCheck.PositiveInt count,FsCheck.PositiveInt scaling)
    {
        var m=1+count.Get%32;var scale=1+scaling.Get%16;
        var plan=Prepare($"L1 b 0 L=20m M={m} SCALE={scale}");
        if(!plan.Success)return false;
        var effective=plan.Operations[0].Requests[0].Circuit.Elements.OfType<Inductor>().Single().Henries;
        return Math.Abs(effective*m/scale-.02)<1e-14;
    }

    [RequestedNativeFact]
    public async Task Temperature_and_multiplicity_execute_all_three_analyses_against_ngspice()
    {
        var parent=Environment.GetEnvironmentVariable("SPICE_REQUESTED_ARTIFACTS")!;Assert.True(Directory.Exists(parent));
        var folder=Path.Combine(parent,"inductor-oracle-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
        const string text="Coil oracle\nV1 a 0 DC 0 AC 1 PWL(0 0 1u 1)\nR1 a b 1k\nL1 b 0 coil 20m M=2 SCALE=1\n"
            +".model coil L(IND=40m TC1=.01 TNOM=27)\n.temp 37\n.op\n.ac lin 3 10 100\n.tran 10n 1u 0 10n\n.end\n";
        var input=Path.Combine(folder,"coil.cir");await File.WriteAllTextAsync(input,text);
        var inventory=Path.Combine(folder,"inventory.json");await File.WriteAllTextAsync(inventory,JsonSerializer.Serialize(new {rows=new[]{new {
            scope="ngspice",path="coil.cir",result=new {Sha256=Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(input)))}
        }}}));
        var root=Directory.GetParent(Path.GetDirectoryName(SourceFile())!)!.Parent!.FullName;var output=Path.Combine(folder,"output");
        await RequestedCorpus.RunAsync([root,folder,Environment.GetEnvironmentVariable("SPICE_CORPUS_ENGINE")!,
            Environment.GetEnvironmentVariable("SPICE_CORPUS_NGSPICE")!,inventory,output,"120"]);
        using var report=JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(output,"summary.json")));
        var row=report.RootElement.GetProperty("rows")[0];Assert.True(row.GetProperty("native_agreed").GetBoolean(),row.GetRawText());
        Assert.Equal(3,row.GetProperty("operations").GetArrayLength());
    }

    private static string SourceFile([System.Runtime.CompilerServices.CallerFilePath]string path="")=>path;
}
