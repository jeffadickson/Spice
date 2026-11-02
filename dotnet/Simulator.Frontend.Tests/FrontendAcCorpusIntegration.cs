using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Xunit;
namespace Simulator.Frontend.Tests;

/// <summary>Explicit native/fixture prerequisites; missing tools remain skipped evidence.</summary>
public sealed class FrontendAcCorpusFactAttribute : FrontendCorpusFactAttribute
{
    public FrontendAcCorpusFactAttribute()
    {
        if(new[]{"SPICE_FRONTEND_AC_DLL","SPICE_FRONTEND_FAULT_ENGINE"}.Any(k=>!File.Exists(Environment.GetEnvironmentVariable(k))))
            Skip="Set AC corpus DLL and synthetic fault executable in addition to the OP corpus prerequisites.";
    }
}
public sealed class FrontendAcCorpusIntegration
{
    [FrontendAcCorpusFact]
    public async Task Complex_phase_matches_native_and_malformed_AC_preserves_both_provider_failures()
    {
        var parent=Environment.GetEnvironmentVariable("SPICE_CORPUS_TEST_ROOT")!;
        Assert.True(Directory.Exists(parent));
        var folder=Path.Combine(parent,"ac-integration-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
        var deck=Path.Combine(folder,"phase.cir");File.WriteAllText(deck,"Phase\nV1 a 0 DC 2 AC 1 -90\nR1 a 0 1k\n.end\n");
        var inventory=Path.Combine(folder,"inventory.json");
        File.WriteAllText(inventory,JsonSerializer.Serialize(new{rows=new[]{new{scope="ngspice",path="phase.cir",result=new{Sha256=Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(deck)))}}}}));
        var root=Environment.GetEnvironmentVariable("SPICE_FRONTEND_REPOSITORY")!;
        var engine=Environment.GetEnvironmentVariable("SPICE_CORPUS_ENGINE")!;var ng=Environment.GetEnvironmentVariable("SPICE_CORPUS_NGSPICE")!;
        var op=Path.Combine(folder,"op");
        Assert.Equal(0,await Run(Environment.GetEnvironmentVariable("SPICE_FRONTEND_CORPUS_DLL")!,[root,folder,engine,ng,inventory,op,"selected-linear-op"],"op"));
        var ac=Path.Combine(folder,"ac");var helper=Environment.GetEnvironmentVariable("SPICE_FRONTEND_AC_DLL")!;
        Assert.Equal(0,await Run(helper,[root,op,engine,ng,ac],"ac"));
        using(var document=JsonDocument.Parse(File.ReadAllText(Path.Combine(ac,"summary.json"))))
        {
            var row=Assert.Single(document.RootElement.GetProperty("rows").EnumerateArray());
            Assert.Equal("compared_pass",row.GetProperty("status").GetString());
            Assert.Equal(6,row.GetProperty("checks").GetArrayLength());
            foreach(var check in row.GetProperty("checks").EnumerateArray())
            {
                Assert.True(check.GetProperty("passed").GetBoolean());
                Assert.InRange(Math.Abs(check.GetProperty("actual_real").GetDouble()),0,1e-12);
                Assert.InRange(check.GetProperty("actual_imag").GetDouble(),-1-1e-12,-1+1e-12);
            }
        }
        foreach(var mode in new[]{"ac-short","ac-extra","ac-nonfinite"})
        {
            var output=Path.Combine(folder,mode);
            Assert.Equal(1,await Run(helper,[root,op,Environment.GetEnvironmentVariable("SPICE_FRONTEND_FAULT_ENGINE")!,ng,output],mode,mode));
            using var document=JsonDocument.Parse(File.ReadAllText(Path.Combine(output,"summary.json")));
            Assert.Equal("completed_with_results",document.RootElement.GetProperty("state").GetString());
            var row=Assert.Single(document.RootElement.GetProperty("rows").EnumerateArray());
            Assert.Equal("provider_failure",row.GetProperty("status").GetString());
            Assert.Equal(2,row.GetProperty("provider_errors").GetArrayLength());
            Assert.Equal(0,row.GetProperty("checks").GetArrayLength());
            Assert.Equal(0,row.GetProperty("completed_providers").GetInt32());
        }
        async Task<int> Run(string helper,string[] args,string label,string? fault=null)
        {
            var start=new ProcessStartInfo("dotnet"){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
            start.ArgumentList.Add(helper);foreach(var arg in args)start.ArgumentList.Add(arg);
            if(fault!=null)start.Environment["SPICE_FRONTEND_TEST_FAULT"]=fault;
            using var child=Process.Start(start)!;var stdout=child.StandardOutput.ReadToEndAsync();var stderr=child.StandardError.ReadToEndAsync();
            using var limit=new CancellationTokenSource(TimeSpan.FromMinutes(2));
            try{await child.WaitForExitAsync(limit.Token);}catch(OperationCanceledException){child.Kill(true);await child.WaitForExitAsync();throw;}
            File.WriteAllText(Path.Combine(folder,label+".stdout"),await stdout);File.WriteAllText(Path.Combine(folder,label+".stderr"),await stderr);
            return child.ExitCode;
        }
    }
}
