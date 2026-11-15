using System.Security.Cryptography;
using System.Text.Json;
using Simulator.Client;
using Simulator.Frontend;
using Simulator.RequestedQualification;
using Simulator.Syntax;
using Xunit;
namespace Simulator.Frontend.Tests;

public sealed class ReferenceGridTheoryAttribute:TheoryAttribute
{
    public ReferenceGridTheoryAttribute()
    {
        if(!Directory.Exists(Environment.GetEnvironmentVariable("SPICE_REFERENCE_GRID_BASELINE"))
            ||!Directory.Exists(Environment.GetEnvironmentVariable("SPICE_REFERENCE_GRID_OUTPUT"))
            ||!File.Exists(Environment.GetEnvironmentVariable("SPICE_REFERENCE_GRID_ENGINE")))
            Skip="Set completed immutable RC/RLC baselines, engine and E: output directory.";
    }
}

/// <summary>Fresh native baselines are external prerequisites, never generated gold numbers.</summary>
public sealed class ReferenceGridReplayNativeTests
{
    [ReferenceGridTheory][InlineData("lu")]
    public async Task Linked_engine_refuses_an_omitted_source_corner(string backend)
    {
        // A cached mutant can survive restoration of source text. Exercise the
        // final linked executable's negative contract as well as the library.
        var plan=RequestedAnalysis.Prepare(DeckParser.Parse("corner\nV1 in 0 PWL(0 0 .25 1 1 0)\nR1 in 0 2\n.tran .5 1\n.end\n",collectDeclarations:true),backend:backend);
        Assert.True(plan.Success);var original=Assert.Single(Assert.Single(plan.Operations).Requests);
        var options=Assert.IsType<TransientConfigured>(original.Analysis).Options;
        var request=original with {Analysis=new TransientConfigured(options with {Adaptive=null,MaxStepS=0.5,MaxSteps=10,AcceptedTimesS=[0,0.5,1]})};
        using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(120));
        var response=await new SimulationClient(Environment.GetEnvironmentVariable("SPICE_REFERENCE_GRID_ENGINE")!).RunAsync(request,deadline.Token);
        await File.WriteAllTextAsync(Path.Combine(Environment.GetEnvironmentVariable("SPICE_REFERENCE_GRID_OUTPUT")!,"corner-"+Guid.NewGuid().ToString("N")+".json"),response.Payload.GetRawText());
        Assert.False(response.Succeeded);
        Assert.Contains("omits a source breakpoint",response.Payload.GetRawText(),StringComparison.Ordinal);
    }
    [ReferenceGridTheory][InlineData("lu",0)][InlineData("lu",1)][InlineData("faer",0)][InlineData("faer",1)]
    public async Task Complete_linear_physics_agrees_at_every_original_ngspice_time(string backend,int index)
    {
        var root=Environment.GetEnvironmentVariable("SPICE_REFERENCE_GRID_BASELINE")!;
        var engine=Path.GetFullPath(Environment.GetEnvironmentVariable("SPICE_REFERENCE_GRID_ENGINE")!);
        var folder=Path.Combine(root,"baseline-"+backend);
        using var summary=JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(folder,"summary.json")));
        Assert.True(summary.RootElement.GetProperty("provenance_valid").GetBoolean());
        Assert.Equal(Hash(engine),summary.RootElement.GetProperty("source_hashes").GetProperty(engine).GetString()!.ToLowerInvariant());
        var row=summary.RootElement.GetProperty("rows")[index];var input=row.GetProperty("input_path").GetString()!;
        Assert.Equal(Hash(input),row.GetProperty("input_sha256").GetString()!.ToLowerInvariant());
        var plan=RequestedAnalysis.Prepare(DeckParser.Parse(await File.ReadAllTextAsync(input),collectDeclarations:true),backend:backend);
        Assert.True(plan.Success);var operation=Assert.Single(plan.Operations.Where(o=>o.Kind=="tran"));
        var ordinal=plan.Operations.IndexOf(operation);var raw=Path.Combine(folder,"cases",index.ToString("D4"),"requested_"+ordinal,"native.raw");
        Assert.Equal(Hash(raw),row.GetProperty("operations")[ordinal].GetProperty("native_raw_sha256").GetString()!.ToLowerInvariant());
        var native=AsciiRaw.Read(await File.ReadAllTextAsync(raw));
        using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(120));
        var replay=await ReferenceGridReplay.RunAsync(operation,plan.CircuitPreparation!,native,new(engine),deadline.Token);
        var output=Path.Combine(Environment.GetEnvironmentVariable("SPICE_REFERENCE_GRID_OUTPUT")!,backend+"-"+index+"-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(output);
        await File.WriteAllTextAsync(Path.Combine(output,"response.json"),replay.Response.Payload.GetRawText());
        await File.WriteAllTextAsync(Path.Combine(output,"comparison.json"),JsonSerializer.Serialize(replay.Comparison));
        var manifest=replay.Response.ReadManifest();
        Assert.Equal(native.Points.Select(p=>p[0].Real),manifest.Samples.Select(p=>p.TimeS));
        Assert.All(replay.Comparison.Observables,o=>Assert.Equal(native.Points.Length,o.Points));
        Assert.Equal(0,replay.Comparison.ExcludedStartupSamples);
        Assert.True(replay.Comparison.Agreed,JsonSerializer.Serialize(replay.Comparison));
        Assert.NotNull(Assert.IsType<TransientConfigured>(operation.Requests[0].Analysis).Options.Adaptive);
    }
    private static string Hash(string file)=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file))).ToLowerInvariant();
}
