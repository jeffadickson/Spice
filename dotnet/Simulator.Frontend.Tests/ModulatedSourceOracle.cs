using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Simulator.Client;
using Simulator.Syntax;
using Xunit;
namespace Simulator.Frontend.Tests;

/// <summary>Original AM/SFFM cards through generated preparation and both native CPU providers.</summary>
public sealed class ModulatedSourceOracle
{
 [PreparationNativeFact]
 public async Task Voltage_and_current_modulation_match_original_native_samples()
 {
  var parent=Environment.GetEnvironmentVariable("SPICE_CORPUS_TEST_ROOT")!;
  var root=Path.Combine(parent,"modulation-oracle-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
  var profiles=new[]{"SFFM(0 2 5 .25 1 .01 0 0)","AM(0 .5 .2 1 5 .01 0 0)","SFFM(.1 2 5 .25 1 0 10 -20)","AM(.1 .5 .2 1 5 0 10 -20)","SFFM(0 2 5 90 10 0 0 0)","SFFM(0 2)","AM(0 .5)"};
  foreach(var profile in profiles)foreach(var current in new[]{false,true})
  {
   var name=(current?"I":"V")+Array.IndexOf(profiles,profile);var directory=Path.Combine(root,name);Directory.CreateDirectory(directory);
   // A one-ohm load makes V(out) numerically equal to the current flowing
   // from ground into out, providing an independent polarity/unit check.
   var body=(current?"I1 0 out ":"V1 out 0 ")+profile+"\nR1 out 0 1\n";
   var syntax=DeckParser.Parse(name+"\n"+body+".end\n",collectDeclarations:true);
   var selected=TransientSelection.Prepare(syntax,.1,.001,200);Assert.True(selected.Success,string.Join(',',selected.Prepared.Diagnostics.Select(d=>d.Code)));
   var data=Path.Combine(directory,"native.dat");var deck=Path.Combine(directory,"native.cir");
   await File.WriteAllTextAsync(deck,name+"\n"+body+".control\nset numdgt=17\ntran .001 .1 0 .00001\nwrdata "+data.Replace('\\','/')+" v(out)\nquit\n.endc\n.end\n");
   var start=new ProcessStartInfo(Environment.GetEnvironmentVariable("SPICE_CORPUS_NGSPICE")!){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true,WorkingDirectory=directory};
   foreach(var argument in new[]{"-n","-b",deck})start.ArgumentList.Add(argument);
   using(var process=Process.Start(start)!)
   {
    var stdout=process.StandardOutput.ReadToEndAsync();var stderr=process.StandardError.ReadToEndAsync();using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(30));
    try{await process.WaitForExitAsync(timeout.Token);}catch{if(!process.HasExited)process.Kill(true);await process.WaitForExitAsync();throw;}
    await File.WriteAllTextAsync(Path.Combine(directory,"native.log"),await stdout+"\n"+await stderr);Assert.Equal(0,process.ExitCode);
   }
   var native=(await File.ReadAllLinesAsync(data)).Where(l=>!string.IsNullOrWhiteSpace(l)).Select(l=>l.Split((char[]?)null,StringSplitOptions.RemoveEmptyEntries).Select(v=>double.Parse(v,CultureInfo.InvariantCulture)).ToArray()).ToArray();
   Assert.True(native.Length>101);
   // A resistor/source circuit has an exact algebraic waveform. Validate both
   // actual grids against it; interpolation across the native delay corner
   // would measure interpolation error rather than source-evaluation error.
   var profileIndex=Array.IndexOf(profiles,profile);
   double Expected(double time)
   {
    var delay=profileIndex<2?.01:0;if(time<=delay)return 0;
    var t=time-delay;var phased=profileIndex is 2 or 3;
    var carrier=2*Math.PI*(profileIndex==5?50:profileIndex==6?5000:5)*t+(phased?-20*Math.PI/180:0);
    var modulation=2*Math.PI*(profileIndex==4?10:profileIndex==5?5000:profileIndex==6?50:1)*t+(phased?10*Math.PI/180:0);
    var offset=phased?.1:0;
    return profileIndex is 1 or 3 or 6?offset+(.5+(profileIndex==6?1:.2)*Math.Sin(modulation))*Math.Sin(carrier)
       :offset+2*Math.Sin(carrier+(profileIndex==4?.5:profileIndex==5?.01:.25)*Math.Sin(modulation));
   }
   foreach(var row in native){Assert.True(double.IsFinite(row[0])&&double.IsFinite(row[1]));Assert.InRange(Math.Abs(row[1]-Expected(row[0])),0,1e-9);}
   foreach(var backend in new[]{"lu","faer"})
   {
    var request=selected.Prepared.Request! with{Backend=backend};using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(30));
    await File.WriteAllTextAsync(Path.Combine(directory,backend+".request.json"),JsonSerializer.Serialize(request,WireFormat.Options));
    var response=await new SimulationClient(Environment.GetEnvironmentVariable("SPICE_CORPUS_ENGINE")!).RunAsync(request,timeout.Token);
    await File.WriteAllTextAsync(Path.Combine(directory,backend+".response.json"),response.Payload.GetRawText());Assert.True(response.Succeeded,response.Payload.GetRawText());
    var manifest=response.ReadManifest();var signal=Array.FindIndex(manifest.Signals,s=>s.Id=="v("+selected.Prepared.RootNodes["out"]+")");Assert.True(signal>=0);
    Assert.Equal(101,manifest.Samples.Length);
    foreach(var sample in manifest.Samples)
    {
     Assert.InRange(Math.Abs(Expected(sample.TimeS)-sample.Values[signal]),0,1e-9);
    }
   }
  }
 }
}
