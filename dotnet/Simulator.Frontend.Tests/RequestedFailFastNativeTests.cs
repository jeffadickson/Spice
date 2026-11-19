using System.Security.Cryptography;
using System.Text.Json;
using Simulator.RequestedQualification;
using Xunit;

namespace Simulator.Frontend.Tests;

/// <summary>Failed sweeps retain explicit missing coverage without repeatedly launching a broken operation.</summary>
public sealed class RequestedFailFastNativeTests
{
    [RequestedNativeFact]
    public async Task Failed_sweep_marks_remaining_points_unattempted_and_continues_other_operations_and_decks()
    {
        var parent=Environment.GetEnvironmentVariable("SPICE_REQUESTED_ARTIFACTS")!;
        Assert.True(Directory.Exists(parent));
        var folder=Path.Combine(parent,"fail-fast-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        // Parallel incompatible ideal voltage sources are an independent, structural
        // singular-system witness. No unavailable plugin or fake executable is needed.
        var decks=new Dictionary<string,string> {
            ["failed.cir"]="Singular\nV1 a 0 1\nV2 a 0 3\nR1 a 0 1k\n.dc V1 1 2 .5\n.op\n.end\n",
            ["good.cir"]="Divider\nV1 a 0 2\nR1 a b 1k\nR2 b 0 1k\n.op\n.end\n"
        };
        foreach(var (name,text) in decks)await File.WriteAllTextAsync(Path.Combine(folder,name),text);
        var inventory=Path.Combine(folder,"inventory.json");
        await File.WriteAllTextAsync(inventory,JsonSerializer.Serialize(new {rows=decks.Select(d=>new {
            scope="ngspice",path=d.Key,result=new {Sha256=Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(Path.Combine(folder,d.Key))))}
        })}));
        var root=Directory.GetParent(Path.GetDirectoryName(SourceFile())!)!.Parent!.FullName;
        var output=Path.Combine(folder,"output");
        await RequestedCorpus.RunAsync([root,folder,Environment.GetEnvironmentVariable("SPICE_CORPUS_ENGINE")!,
            Environment.GetEnvironmentVariable("SPICE_CORPUS_NGSPICE")!,inventory,output,"120"]);
        using var report=JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(output,"summary.json")));
        Assert.True(report.RootElement.GetProperty("provenance_valid").GetBoolean());
        var rows=report.RootElement.GetProperty("rows");
        var failed=rows[0];Assert.False(failed.GetProperty("requested_executed").GetBoolean());
        var operations=failed.GetProperty("operations");Assert.Equal(2,operations.GetArrayLength());
        var points=operations[0].GetProperty("points");Assert.Equal(3,points.GetArrayLength());
        Assert.Equal("engine_failed",points[0].GetProperty("status").GetString());
        Assert.All(points.EnumerateArray().Skip(1),point=>Assert.Equal("unattempted_after_operation_failure",point.GetProperty("status").GetString()));
        Assert.Equal("engine_operation_failed_atomically",operations[1].GetProperty("status").GetString());
        Assert.True(rows[1].GetProperty("native_agreed").GetBoolean());
        Assert.Single(Directory.GetFiles(Path.Combine(output,"cases","0000","requested_0"),"*.response.json"));
    }

    private static string SourceFile([System.Runtime.CompilerServices.CallerFilePath]string path="")=>path;
}
