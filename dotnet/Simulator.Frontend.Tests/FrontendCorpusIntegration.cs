using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Xunit;
namespace Simulator.Frontend.Tests;

/// <summary>Explicit external-oracle integration; absence is an honest skip.</summary>
public class FrontendCorpusFactAttribute : FactAttribute
{
    /// <summary>Requires the built file app and existing native executables.</summary>
    public FrontendCorpusFactAttribute()
    {
        if(new[]{"SPICE_FRONTEND_CORPUS_DLL","SPICE_CORPUS_ENGINE","SPICE_CORPUS_NGSPICE"}.Any(key=>!File.Exists(Environment.GetEnvironmentVariable(key)))||
           new[]{"SPICE_FRONTEND_REPOSITORY","SPICE_CORPUS_TEST_ROOT"}.Any(key=>!Directory.Exists(Environment.GetEnvironmentVariable(key))))
            Skip="Set explicit frontend corpus DLL/repository, Rust/ngspice executables and existing test artifact root.";
    }
}
public sealed class FrontendCorpusIntegration
{
    [FrontendCorpusFact]
    public async Task Real_analytical_circuits_and_negative_rows_preserve_actual_execution_outcomes()=>await Run(false);
    [FrontendCorpusFact]
    public async Task Selected_linear_DC_executes_RLC_and_masks_input_programs_explicitly()=>await Run(true);

    [FrontendCorpusFact]
    public async Task No_exposed_nodes_are_unavailable_instead_of_a_numerical_mismatch()=>await Run(true,"zero-port.cir");

    private static async Task Run(bool selected,string? only=null)
    {
        var parent=Environment.GetEnvironmentVariable("SPICE_CORPUS_TEST_ROOT")!;
        var folder=Path.Combine(parent,"frontend-integration-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
        var fixtures=new[]{
            (Name:"divider.cir",Body:"V1 in 0 3\nR1 in out 1k\nR2 out 0 4k\n.op",Expected:"compared_pass"),
            (Name:"hierarchy.cir",Body:".include 'settings.inc'\n.include 'cell.inc'\nV1 in 0 3\nR1 in out 1k\nX1 out 0 load\n.op",Expected:"compared_pass"),
            (Name:"signed-voltage.cir",Body:"V1 a 0 DC -2\nR1 a 0 1k",Expected:"compared_pass"),
            (Name:"signed-current.cir",Body:"I1 a 0 1m\nR1 a 0 1k",Expected:"compared_pass"),
            (Name:"punctuated.cir",Body:"V1 VC+ 0 3\nV2 VC- 0 1\nR1 VC+ VC- 1k",Expected:"compared_pass"),
            // A familiar exclusion PATH with different bytes is never excluded.
            (Name:"README_OSDI_howto",Body:"V1 a 0 2\nR1 a 0 1k",Expected:"compared_pass"),
            (Name:"fraction.cir",Body:"V1 a 0 {143.0/3}\nR1 a 0 1k",Expected:"compared_pass"),
            (Name:"source-ac-only.cir",Body:"V1 a 0 AC 1 -90\nR1 a 0 1k",Expected:selected?"compared_pass":"lowering_refused"),
            (Name:"source-dc-ac.cir",Body:"V1 a 0 DC=2 AC 1\nR1 a 0 1k",Expected:selected?"compared_pass":"lowering_refused"),
            (Name:"source-current.cir",Body:".param bias=2m\nI1 a 0 AC 1 DC {bias}\nR1 a 0 1k",Expected:selected?"compared_pass":"lowering_refused"),
            (Name:"reporting.cir",Body:"V1 a 0 DC 2 AC 1\nR1 a 0 1k\n.option noinit acct noacct",Expected:selected?"compared_pass":"lowering_refused"),
            (Name:"source-mixed.cir",Body:"Vbias a 0 2\nIac b 0 AC 1 DC 0\nR1 a b 1k\nR2 b 0 1k",Expected:selected?"compared_pass":"lowering_refused"),
            (Name:"vcvs.cir",Body:"V1 in 0 DC 2 AC 1 -90\nE1 out 0 in 0 -3\nR1 out 0 1k",Expected:selected?"compared_pass":"lowering_refused"),
            (Name:"vccs.cir",Body:"V1 in 0 DC 2 AC 1\nG1 out 0 in 0 -2m\nR1 out 0 1k",Expected:selected?"compared_pass":"lowering_refused"),
            (Name:"controlled-hierarchy.cir",Body:"V1 in 0 DC 2 AC 1\nX1 in out cell gain=3\n.subckt cell p q gain=2\nE1 q 0 p 0 {gain}\nR1 q 0 1k\n.ends",Expected:selected?"compared_pass":"lowering_refused"),
            (Name:"zero-port.cir",Body:"X1 noports\n.subckt noports\nV1 hidden 0 1\nR1 hidden 0 1k\n.ends",Expected:"solver_completed_no_root_observables"),
            (Name:"capacitor.cir",Body:"V1 a 0 1\nC1 a 0 1u",Expected:selected?"compared_pass":"lowering_refused"),
            (Name:"rlc.cir",Body:"V1 a 0 3\nR1 a b 1k\nL1 b 0 2m\nC1 a b 3u",Expected:selected?"compared_pass":"lowering_refused"),
            (Name:"observations.cir",Body:"V1 a 0 1\nR1 a 0 1k\n.print op v(a)\n.plot op v(a)",Expected:selected?"compared_pass":"lowering_refused"),
            (Name:"control.cir",Body:"V1 a 0 1\nR1 a 0 1k\n.control\necho MUST_NOT_RUN\nop\n.endc",Expected:selected?"compared_pass":"lowering_refused"),
            (Name:"malformed.cir",Body:"R1 a 0",Expected:"resource_refused"),
            (Name:"changed.cir",Body:"V1 a 0 1\nR1 a 0 1k",Expected:"input_changed"),
            (Name:"singular.cir",Body:"R1 a b 1k",Expected:"engine_failure")};
        if(only!=null)fixtures=fixtures.Where(f=>f.Name==only).ToArray();
        var rows=new List<object>();
        foreach(var item in fixtures)
        {
            var file=Path.Combine(folder,item.Name);File.WriteAllText(file,"Owned analytical fixture\n"+item.Body+"\n.end\n");
            var hash=Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(file)));
            rows.Add(new{scope="ngspice",path=item.Name,result=new{Sha256=item.Name=="changed.cir"?new string('0',64):hash}});
        }
        File.WriteAllText(Path.Combine(folder,"settings.inc"),".param r=1k\n");
        File.WriteAllText(Path.Combine(folder,"cell.inc"),".subckt load p n\nR1 p private {3*r}\nR2 private n {r}\n.ends\n");
        var inventory=Path.Combine(folder,"inventory.json");File.WriteAllText(inventory,JsonSerializer.Serialize(new{rows}));
        var output=Path.Combine(folder,"run");
        var start=new ProcessStartInfo("dotnet"){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
        foreach(var arg in new[]{Environment.GetEnvironmentVariable("SPICE_FRONTEND_CORPUS_DLL")!,Environment.GetEnvironmentVariable("SPICE_FRONTEND_REPOSITORY")!,folder,
            Environment.GetEnvironmentVariable("SPICE_CORPUS_ENGINE")!,Environment.GetEnvironmentVariable("SPICE_CORPUS_NGSPICE")!,inventory,output})start.ArgumentList.Add(arg);
        if(selected)start.ArgumentList.Add("selected-linear-op");
        using var process=Process.Start(start)!;var stdout=process.StandardOutput.ReadToEndAsync();var stderr=process.StandardError.ReadToEndAsync();
        using var timeout=new CancellationTokenSource(TimeSpan.FromMinutes(5));
        try{await process.WaitForExitAsync(timeout.Token);}catch(OperationCanceledException){process.Kill(true);await process.WaitForExitAsync();throw;}
        File.WriteAllText(Path.Combine(folder,"stdout.log"),await stdout);File.WriteAllText(Path.Combine(folder,"stderr.log"),await stderr);
        // Deliberate input-change and singular rows make this an expected failed
        // qualification campaign. They must not prevent good rows completing.
        Assert.Equal(fixtures.Any(f=>f.Expected is "input_changed" or "engine_failure")?1:0,process.ExitCode);
        using var report=JsonDocument.Parse(File.ReadAllText(Path.Combine(output,"summary.json")));
        Assert.Equal("completed_with_results",report.RootElement.GetProperty("state").GetString());
        var outcomes=report.RootElement.GetProperty("rows").EnumerateArray().ToDictionary(r=>r.GetProperty("path").GetString()!);
        Assert.Equal(fixtures.Length,outcomes.Count);
        foreach(var item in fixtures)
        {
            var row=outcomes[item.Name];Assert.Equal(item.Expected,row.GetProperty("status").GetString());
            var providers=row.GetProperty("providers").EnumerateArray().ToArray();
            if(item.Expected is "lowering_refused" or "resource_refused" or "input_changed")Assert.Empty(providers);
            if(item.Expected=="compared_pass")
            {
                Assert.Equal(new[]{"lu","faer"},providers.Select(p=>p.GetProperty("backend").GetString()));
                Assert.All(row.GetProperty("comparisons").EnumerateArray(),c=>Assert.True(c.GetProperty("passed").GetBoolean()));
                var expected=item.Name switch{"signed-voltage.cir"=>-2.0,"signed-current.cir"=>-1.0,"README_OSDI_howto"=>2.0,"fraction.cir"=>143.0/3,_=>double.NaN};
                if(double.IsFinite(expected))foreach(var c in row.GetProperty("comparisons").EnumerateArray())
                {Assert.InRange(c.GetProperty("ngspice_volts").GetDouble(),expected-1e-10,expected+1e-10);Assert.InRange(c.GetProperty("rust_volts").GetDouble(),expected-1e-10,expected+1e-10);}
                if(item.Name is "divider.cir" or "hierarchy.cir")foreach(var c in row.GetProperty("comparisons").EnumerateArray().Where(c=>c.GetProperty("node").GetString()=="out"))
                    Assert.InRange(c.GetProperty("rust_volts").GetDouble(),2.4-1e-10,2.4+1e-10);
            }
        }
        if(selected&&only==null)
        {
            foreach(var source in new[]{(Name:"source-ac-only.cir",Expected:0.0),(Name:"source-dc-ac.cir",Expected:2.0),(Name:"source-current.cir",Expected:-2.0)})
                foreach(var observation in outcomes[source.Name].GetProperty("comparisons").EnumerateArray())
                {
                    Assert.InRange(observation.GetProperty("rust_volts").GetDouble(),source.Expected-1e-10,source.Expected+1e-10);
                    Assert.InRange(observation.GetProperty("ngspice_volts").GetDouble(),source.Expected-1e-10,source.Expected+1e-10);
                }
            foreach(var control in new[]{(Name:"vcvs.cir",Expected:-6.0),(Name:"vccs.cir",Expected:4.0),(Name:"controlled-hierarchy.cir",Expected:6.0)})
                foreach(var observation in outcomes[control.Name].GetProperty("comparisons").EnumerateArray().Where(c=>c.GetProperty("node").GetString()=="out"))
                {
                    Assert.InRange(observation.GetProperty("rust_volts").GetDouble(),control.Expected-1e-10,control.Expected+1e-10);
                    Assert.InRange(observation.GetProperty("ngspice_volts").GetDouble(),control.Expected-1e-10,control.Expected+1e-10);
                }
            var index=Array.FindIndex(fixtures,f=>f.Name=="control.cir");
            var directory=Path.Combine(output,"cases",index.ToString("D4",CultureInfo.InvariantCulture));
            Assert.Contains("MUST_NOT_RUN",File.ReadAllText(Path.Combine(directory,"expanded.cir")));
            Assert.DoesNotContain("MUST_NOT_RUN",File.ReadAllText(Path.Combine(directory,"reference.cir")));
            Assert.Contains(outcomes["control.cir"].GetProperty("omissions").EnumerateArray(),o=>o.GetProperty("reason").GetString()=="input_control_not_executed");
            foreach(var observation in outcomes["rlc.cir"].GetProperty("comparisons").EnumerateArray())
            {
                var expected=observation.GetProperty("node").GetString()=="a"?3.0:0.0;
                Assert.InRange(observation.GetProperty("ngspice_volts").GetDouble(),expected-1e-10,expected+1e-10);
                Assert.InRange(observation.GetProperty("rust_volts").GetDouble(),expected-1e-10,expected+1e-10);
            }
        }
    }
}
