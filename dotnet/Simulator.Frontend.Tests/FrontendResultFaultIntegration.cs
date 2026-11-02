using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Xunit;
namespace Simulator.Frontend.Tests;

/// <summary>Opt-in synthetic malformed-result tests, separate from real simulator evidence.</summary>
public sealed class FrontendFaultFactAttribute : FrontendCorpusFactAttribute
{
    /// <summary>Requires the explicitly built synthetic fault executable.</summary>
    public FrontendFaultFactAttribute()
    {
        if(!File.Exists(Environment.GetEnvironmentVariable("SPICE_FRONTEND_FAULT_ENGINE")))Skip="Build and set SPICE_FRONTEND_FAULT_ENGINE to exercise synthetic result faults.";
    }
}
public sealed class FrontendResultFaultIntegration
{
    [FrontendCorpusFact]
    public async Task Invalid_admission_budget_is_rejected_before_creating_a_campaign()
    {
        var parent=Environment.GetEnvironmentVariable("SPICE_CORPUS_TEST_ROOT")!;
        var folder=Path.Combine(parent,"frontend-budget-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
        var output=Path.Combine(folder,"not-created");
        var start=new ProcessStartInfo("dotnet"){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
        foreach(var arg in new[]{Environment.GetEnvironmentVariable("SPICE_FRONTEND_CORPUS_DLL")!,Environment.GetEnvironmentVariable("SPICE_FRONTEND_REPOSITORY")!,folder,
            Environment.GetEnvironmentVariable("SPICE_CORPUS_ENGINE")!,Environment.GetEnvironmentVariable("SPICE_CORPUS_NGSPICE")!,Path.Combine(folder,"missing.json"),output,"selected-linear-op","0"})start.ArgumentList.Add(arg);
        using var process=Process.Start(start)!;var stdout=process.StandardOutput.ReadToEndAsync();var stderr=process.StandardError.ReadToEndAsync();
        using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try{await process.WaitForExitAsync(timeout.Token);}catch(OperationCanceledException){process.Kill(true);await process.WaitForExitAsync();throw;}
        Assert.NotEqual(0,process.ExitCode);Assert.Contains("budget",await stderr,StringComparison.OrdinalIgnoreCase);await stdout;
        Assert.False(Directory.Exists(output));
    }
    [FrontendFaultFact]
    public async Task Malformed_provider_results_and_real_numeric_disagreement_never_become_passes()
    {
        var parent=Environment.GetEnvironmentVariable("SPICE_CORPUS_TEST_ROOT")!;
        foreach(var mode in new[]{"missing","wrong-unit","non-finite","case-id","provider","mismatch"})
        {
            var folder=Path.Combine(parent,"frontend-fault-"+mode+"-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
            var file=Path.Combine(folder,"input.cir");File.WriteAllText(file,"Synthetic fault control\nV1 a 0 1\nR1 a 0 1k\n.op\n.end\n");
            var hash=Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(file)));var inventory=Path.Combine(folder,"inventory.json");
            File.WriteAllText(inventory,JsonSerializer.Serialize(new{rows=new[]{new{scope="ngspice",path="input.cir",result=new{Sha256=hash}}}}));
            var output=Path.Combine(folder,"run");
            var start=new ProcessStartInfo("dotnet"){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
            foreach(var arg in new[]{Environment.GetEnvironmentVariable("SPICE_FRONTEND_CORPUS_DLL")!,Environment.GetEnvironmentVariable("SPICE_FRONTEND_REPOSITORY")!,folder,
                Environment.GetEnvironmentVariable("SPICE_FRONTEND_FAULT_ENGINE")!,Environment.GetEnvironmentVariable("SPICE_CORPUS_NGSPICE")!,inventory,output,"selected-linear-op"})start.ArgumentList.Add(arg);
            start.Environment["SPICE_FRONTEND_TEST_FAULT"]=mode;
            using var process=Process.Start(start)!;var stdout=process.StandardOutput.ReadToEndAsync();var stderr=process.StandardError.ReadToEndAsync();
            using var timeout=new CancellationTokenSource(TimeSpan.FromMinutes(2));
            try{await process.WaitForExitAsync(timeout.Token);}catch(OperationCanceledException){process.Kill(true);await process.WaitForExitAsync();throw;}
            File.WriteAllText(Path.Combine(folder,"stdout.log"),await stdout);File.WriteAllText(Path.Combine(folder,"stderr.log"),await stderr);
            Assert.Equal(1,process.ExitCode);
            using var report=JsonDocument.Parse(File.ReadAllText(Path.Combine(output,"summary.json")));
            var row=Assert.Single(report.RootElement.GetProperty("rows").EnumerateArray());
            Assert.Equal(mode=="mismatch"?"numerical_mismatch":"engine_failure",row.GetProperty("status").GetString());
            var providers=row.GetProperty("providers").EnumerateArray().ToArray();Assert.Equal(2,providers.Length);
            if(mode!="mismatch")Assert.All(providers,p=>Assert.Equal("transport_or_result_failure",p.GetProperty("status").GetString()));
            else Assert.All(row.GetProperty("comparisons").EnumerateArray(),c=>{Assert.False(c.GetProperty("passed").GetBoolean());Assert.Equal(1,c.GetProperty("ngspice_volts").GetDouble());});
        }
    }
}
