using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Xunit;
namespace Simulator.Frontend.Tests;
/// <summary>Checks the motivating two-file PSpice circuit through generated preparation and native references.</summary>
public sealed class PspiceSwitchOracleIntegration
{
 [FrontendAcCorpusFact]
 public async Task Bug883_and_control_modulation_match_native_on_both_CPU_providers()
 {
  var root=Environment.GetEnvironmentVariable("SPICE_FRONTEND_REPOSITORY")!;
  var folder=Path.Combine(Environment.GetEnvironmentVariable("SPICE_CORPUS_TEST_ROOT")!,"switch-oracle-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
  var rows=new List<object>();
  foreach(var name in new[]{"bug883-vswitch.cir","bug883-vswitch.lib"})File.Copy(Path.Combine(root,"compat/ngspice",name),Path.Combine(folder,name));
  var control="Control modulation\nVDR IN 0 DC .1 AC 0\nVC CTRL 0 DC .1 AC 1\nS1 IN OUT CTRL 0 MODE\n.model MODE VSWITCH(RON=100 ROFF=10000 VON=.2 VOFF=0)\nR1 OUT 0 1k\n.end\n";
  File.WriteAllText(Path.Combine(folder,"control.cir"),control);
  File.WriteAllText(Path.Combine(folder,"quarter.cir"),control.Replace("DC .1 AC 0","DC .1 AC 1",StringComparison.Ordinal).Replace("DC .1 AC 1\nS1","DC .05 AC 0\nS1",StringComparison.Ordinal));
  File.WriteAllText(Path.Combine(folder,"three-quarter.cir"),control.Replace("DC .1 AC 0","DC .1 AC 1",StringComparison.Ordinal).Replace("DC .1 AC 1\nS1","DC .15 AC 0\nS1",StringComparison.Ordinal));
  File.WriteAllText(Path.Combine(folder,"reverse.cir"),File.ReadAllText(Path.Combine(folder,"quarter.cir")).Replace("VON=.2 VOFF=0","VON=0 VOFF=.2",StringComparison.Ordinal));
  foreach(var name in new[]{"bug883-vswitch.cir","control.cir","quarter.cir","three-quarter.cir","reverse.cir"})rows.Add(new{scope="ngspice",path=name,result=new{Sha256=Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(Path.Combine(folder,name))))}});
  var inventory=Path.Combine(folder,"inventory.json");File.WriteAllText(inventory,JsonSerializer.Serialize(new{rows}));
  var engine=Environment.GetEnvironmentVariable("SPICE_CORPUS_ENGINE")!;var ng=Environment.GetEnvironmentVariable("SPICE_CORPUS_NGSPICE")!;
  var op=Path.Combine(folder,"op");
  Assert.Equal(0,await Run("dotnet",[Environment.GetEnvironmentVariable("SPICE_FRONTEND_CORPUS_DLL")!,root,folder,engine,ng,inventory,op,"selected-static-op"],"op"));
  var ac=Path.Combine(folder,"ac");
  Assert.Equal(0,await Run("dotnet",[Environment.GetEnvironmentVariable("SPICE_FRONTEND_AC_DLL")!,root,op,engine,ng,ac],"ac"));
  using(var report=JsonDocument.Parse(File.ReadAllText(Path.Combine(ac,"summary.json"))))
  {
   Assert.Equal(5,report.RootElement.GetProperty("rows").GetArrayLength());
   foreach(var row in report.RootElement.GetProperty("rows").EnumerateArray()){
    Assert.Equal("compared_pass",row.GetProperty("status").GetString());
    var output=row.GetProperty("checks").EnumerateArray().Where(c=>c.GetProperty("node").GetString()!.Equals("OUT",StringComparison.OrdinalIgnoreCase)).ToArray();Assert.Equal(6,output.Length);
    var name=row.GetProperty("path").GetString();
    var t=name=="quarter.cir"?.25:name is "three-quarter.cir" or "reverse.cir"?.75:.5;
    var resistance=Math.Exp(Math.Log(10000)+(Math.Log(100)-Math.Log(10000))*t*t*(3-2*t));
    var expected=name=="control.cir"?.1*1.5*Math.Log(100)/.2/4:1000/(1000+resistance);
    foreach(var c in output){Assert.True(c.GetProperty("passed").GetBoolean());Assert.InRange(c.GetProperty("actual_real").GetDouble(),expected-1e-8,expected+1e-8);Assert.InRange(Math.Abs(c.GetProperty("actual_imag").GetDouble()),0,1e-10);}
   }
  }
  // Also execute the original .99/1/1.01Hz native sweep with the original include
  // boundary and ps mode. This is separate from selected1/100/1000Hz probes.
  var nativeLog=Path.Combine(folder,"original.log");
  Assert.Equal(0,await Run(ng,["-n","-D","ngbehavior=ps","-b","-o",nativeLog,Path.Combine(folder,"bug883-vswitch.cir")],"original"));
  var text=File.ReadAllText(nativeLog);Assert.Matches(@"m_re\s*=\s*5\.0+e-01",text);Assert.Matches(@"m_im\s*=\s*0\.0+e\+00",text);
  async Task<int> Run(string exe,string[] args,string label){
   var start=new ProcessStartInfo(exe){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true,WorkingDirectory=folder};foreach(var arg in args)start.ArgumentList.Add(arg);
   using var p=Process.Start(start)!;var stdout=p.StandardOutput.ReadToEndAsync();var stderr=p.StandardError.ReadToEndAsync();using var limit=new CancellationTokenSource(TimeSpan.FromMinutes(3));
   try{await p.WaitForExitAsync(limit.Token);}catch(OperationCanceledException){p.Kill(true);await p.WaitForExitAsync();throw;}
   File.WriteAllText(Path.Combine(folder,label+".stdout"),await stdout);File.WriteAllText(Path.Combine(folder,label+".stderr"),await stderr);return p.ExitCode;
  }
 }
}
