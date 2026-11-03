using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;
namespace Simulator.Frontend.Tests;

/// <summary>Optional process-isolated independent-reference contract checks.</summary>
public sealed class ThirdReferenceFactAttribute:FactAttribute
{
    /// <summary>Requires explicit existing external worker and artifact paths.</summary>
    public ThirdReferenceFactAttribute()
    {
        if(!File.Exists(Environment.GetEnvironmentVariable("SPICE_THIRD_WORKER_DLL"))||
           !File.Exists(Environment.GetEnvironmentVariable("SPICE_THIRD_PATCH"))||
           !Directory.Exists(Environment.GetEnvironmentVariable("SPICE_CORPUS_TEST_ROOT")))
            Skip="Set third-reference worker, explicit patch manifest and artifact root.";
    }
}

/// <summary>Independent analytical signs and atomic failure semantics at the worker boundary.</summary>
public sealed class ThirdReferenceContracts
{
    [ThirdReferenceFact]
    public async Task Analytical_voltage_and_P_to_N_current_are_exported_with_units()
    {
        var folder=Folder();var deck=Path.Combine(folder,"input.cir");
        File.WriteAllText(deck,"Analytical divider\nV1 in 0 1\nR1 in 0 1k\n.op\n.end\n");
        var data=await Run(folder,Job(deck));
        Assert.Equal("executed",data["status"]!.GetValue<string>());
        Assert.Equal("V",data["probes"]![0]!["unit"]!.GetValue<string>());
        Assert.Equal("A",data["probes"]![1]!["unit"]!.GetValue<string>());
        Assert.Single(data["samples"]!.AsArray());
        var values=data["samples"]![0]!["values"]!;
        Assert.Equal(1,values["v"]!["real"]!.GetValue<double>(),12);
        Assert.Equal(-.001,values["i"]!["real"]!.GetValue<double>(),12);
    }

    [ThirdReferenceFact]
    public async Task Invalid_jobs_never_publish_partial_reference_samples()
    {
        var folder=Folder();var deck=Path.Combine(folder,"input.cir");
        File.WriteAllText(deck,"Negative boundary\nV1 in 0 1\nR1 in 0 1k\n.op\n.end\n");
        var cases=new Action<JsonObject>[] {
            j=>j["mode"]="dc_sweep",j=>j["case_id"]="",
            j=>j["deck_path"]=Path.Combine(folder,"missing.cir"),
            j=>j["patch_manifest"]=Path.Combine(folder,"missing-patch.json"),
            j=>j["probes"]=new JsonArray(),
            j=>j["probes"]![1]!["id"]="v",
            j=>j["probes"]![0]!["kind"]="internal_state",
            j=>j["probes"]![0]!["target"]=""
        };
        foreach(var change in cases){var job=Job(deck);change(job);var result=await Run(folder,job);Assert.NotEqual("executed",result["status"]!.GetValue<string>());Assert.Empty(result["samples"]!.AsArray());}
        var malformed=Path.Combine(folder,"bad.cir");File.WriteAllText(malformed,"Bad\nR1\n.op\n.end\n");
        var bad=await Run(folder,Job(malformed));Assert.NotEqual("executed",bad["status"]!.GetValue<string>());Assert.Empty(bad["samples"]!.AsArray());
        // Duplicate analyses must refuse, even when each individual card is valid.
        var multiple=Path.Combine(folder,"multiple.cir");File.WriteAllText(multiple,"Multiple\nV1 in 0 1\nR1 in 0 1k\n.op\n.ac lin 1 1 1\n.end\n");
        var ambiguous=await Run(folder,Job(multiple));Assert.NotEqual("executed",ambiguous["status"]!.GetValue<string>());Assert.Empty(ambiguous["samples"]!.AsArray());
    }
    private static string Folder(){var parent=Environment.GetEnvironmentVariable("SPICE_CORPUS_TEST_ROOT")!;if(!Directory.Exists(parent))throw new DirectoryNotFoundException(parent);var folder=Path.Combine(parent,"reference-contract-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);return folder;}
    private static JsonObject Job(string deck)=>new(){["case_id"]="contracts",["mode"]="op",["deck_path"]=deck,["patch_manifest"]=Environment.GetEnvironmentVariable("SPICE_THIRD_PATCH"),["probes"]=new JsonArray(new JsonObject{["id"]="v",["kind"]="voltage",["target"]="in"},new JsonObject{["id"]="i",["kind"]="current",["target"]="V1"})};
    private static async Task<JsonObject> Run(string folder,JsonObject job)
    {
        var id=Guid.NewGuid().ToString("N");var input=Path.Combine(folder,id+".job.json");var output=Path.Combine(folder,id+".result.json");File.WriteAllText(input,job.ToJsonString());
        var start=new ProcessStartInfo("dotnet"){UseShellExecute=false,CreateNoWindow=true,RedirectStandardError=true,RedirectStandardOutput=true};
        foreach(var a in new[]{Environment.GetEnvironmentVariable("SPICE_THIRD_WORKER_DLL")!,input,output})start.ArgumentList.Add(a);
        using var child=Process.Start(start)!;var stdout=child.StandardOutput.ReadToEndAsync();var stderr=child.StandardError.ReadToEndAsync();using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try{await child.WaitForExitAsync(deadline.Token);}catch(OperationCanceledException){child.Kill(true);await child.WaitForExitAsync();throw;}
        File.WriteAllText(Path.Combine(folder,id+".log"),await stdout+"\n"+await stderr);Assert.Equal(0,child.ExitCode);Assert.True(File.Exists(output));
        return JsonNode.Parse(File.ReadAllText(output))!.AsObject();
    }
}
