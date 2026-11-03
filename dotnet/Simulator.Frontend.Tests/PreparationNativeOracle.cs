using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using Simulator.Client;
using Simulator.Syntax;
using Xunit;
namespace Simulator.Frontend.Tests;

/// <summary>Original-source native qualification with explicit external prerequisites.</summary>
public sealed class PreparationNativeFactAttribute:FactAttribute
{
    /// <summary>Missing oracle, engine or artifact root is an explicit skip.</summary>
    public PreparationNativeFactAttribute(){if(!File.Exists(Environment.GetEnvironmentVariable("SPICE_CORPUS_ENGINE"))||!File.Exists(Environment.GetEnvironmentVariable("SPICE_CORPUS_NGSPICE"))||!Directory.Exists(Environment.GetEnvironmentVariable("SPICE_CORPUS_TEST_ROOT")))Skip="Set existing native engine, ngspice and artifact root.";}
}
/// <summary>Bridge requests compared to independent original-card ngspice voltages.</summary>
public sealed class PreparationNativeOracle
{
    [PreparationNativeFact]
    public async Task Current_control_and_active_globals_match_original_source_on_both_CPU_providers()
    {
        var cases=new[]{
            (Name:"fh-forward",Body:"F1 out 0 V1 2\nH1 hv 0 V1 3k\nV1 drive 0 1\nR1 drive 0 1k\nR2 out 0 1k\nR3 hv 0 1k",Expected:2d),
            (Name:"f-ecurrent",Body:"V1 drive 0 3\nE1 ctrl 0 drive 0 2\nR1 ctrl 0 1k\nF1 out 0 E1 2\nR2 out 0 1k",Expected:12d),
            (Name:"global-formal",Body:".global vdd\nV1 vdd 0 3\nX1 unused out cell\n.subckt cell vdd out\nR1 vdd out 1k\nR2 out 0 1k\n.ends",Expected:1.5),
            (Name:"global-inactive",Body:"V1 vdd 0 3\nX1 out cell\n.subckt unused\n.global vdd\n.ends\n.subckt cell out\nR1 vdd out 1k\nR2 out 0 1k\n.ends",Expected:0d),
            (Name:"global-active",Body:"V1 vdd 0 3\nX0 unused\nX1 out cell\n.subckt unused\n.global vdd\n.ends\n.subckt cell out\nR1 vdd out 1k\nR2 out 0 1k\n.ends",Expected:1.5),
            (Name:"gain-offset",Body:"V1 in 0 2\nA1 in out amp\n.model amp gain(in_offset=.5 gain=3 out_offset=-1)\nR1 out 0 1k",Expected:6.5)
        };
        var parent=Environment.GetEnvironmentVariable("SPICE_CORPUS_TEST_ROOT")!;
        var root=Path.Combine(parent,"preparation-oracle-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        foreach(var item in cases){
            var text=item.Name+"\n"+item.Body+"\n.end\n";
            var syntax=DeckParser.Parse(text,collectDeclarations:true);var selected=OperatingPointSelection.Select(syntax);
            Assert.True(selected.Success,string.Join(',',selected.Prepared.Diagnostics.Select(d=>d.Code)));
            var file=Path.Combine(root,item.Name+".cir");File.WriteAllText(file,item.Name+"\n"+item.Body+"\n.control\nset numdgt=17\nop\nlet target = v(out)\nprint target\nquit\n.endc\n.end\n");
            var start=new ProcessStartInfo(Environment.GetEnvironmentVariable("SPICE_CORPUS_NGSPICE")!){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
            foreach(var arg in new[]{"-n","-D","ngbehavior=psa","-b",file})start.ArgumentList.Add(arg);
            using var child=Process.Start(start)!;var stdout=child.StandardOutput.ReadToEndAsync();var stderr=child.StandardError.ReadToEndAsync();using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(30));
            try{await child.WaitForExitAsync(deadline.Token);}catch{if(!child.HasExited)child.Kill(true);await child.WaitForExitAsync();throw;}
            var log=await stdout+"\n"+await stderr;File.WriteAllText(Path.Combine(root,item.Name+".log"),log);Assert.Equal(0,child.ExitCode);
            var matches=Regex.Matches(log,@"(?m)^\s*target\s*=\s*([^\s]+)\s*$");Assert.Single(matches);
            var native=double.Parse(matches[0].Groups[1].Value,NumberStyles.Float,CultureInfo.InvariantCulture);Assert.True(double.IsFinite(native));Assert.InRange(Math.Abs(native-item.Expected),0,1e-10);
            foreach(var backend in new[]{"lu","faer"}){
                var response=await new SimulationClient(Environment.GetEnvironmentVariable("SPICE_CORPUS_ENGINE")!).RunAsync(selected.Prepared.Request! with{Backend=backend},deadline.Token);
                File.WriteAllText(Path.Combine(root,item.Name+"-"+backend+".json"),response.Payload.GetRawText());Assert.True(response.Succeeded,response.Payload.GetRawText());
                Assert.InRange(Math.Abs(response.FinalValue("v("+selected.Prepared.RootNodes["out"]+")")-native),0,1e-9+1e-8*Math.Abs(native));
            }
        }
    }
}
