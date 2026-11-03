using System.Diagnostics;
using System.Globalization;
using Simulator.Client;
using Simulator.Syntax;
using Xunit;
namespace Simulator.Frontend.Tests;
public sealed class RequestedNativeFactAttribute:FactAttribute
{
 public RequestedNativeFactAttribute(){if(!File.Exists(Environment.GetEnvironmentVariable("SPICE_CORPUS_ENGINE"))||!File.Exists(Environment.GetEnvironmentVariable("SPICE_CORPUS_NGSPICE"))||!Directory.Exists(Environment.GetEnvironmentVariable("SPICE_REQUESTED_ARTIFACTS")))Skip="Set existing engine/ngspice executables and requested artifact directory.";}
}
public sealed class RequestedAnalysisNativeTests
{
 [RequestedNativeFact]
 public async Task Original_directives_and_repetitions_agree_with_native_on_their_original_grids()
 {
  const string circuit="original requested analysis\nV1 a 0 DC 0 AC 1 PWL(0 0 4u 4)\nR1 a out 1k\nR2 out 0 1k\n";
  string[] cards=[".op",".dc V1 -1 1 .5",".dc V1 1 -1 -.5",".ac lin 3 10 30",".ac dec 2 10 1k",".tran 1u 4u",".op",".ac oct 2 10 40",".tran .5u 4u 0 .1u"];
  var plan=RequestedAnalysis.Prepare(DeckParser.Parse(circuit+string.Join('\n',cards)+"\n.end\n",collectDeclarations:true));
  Assert.True(plan.Success,string.Join(',',plan.Diagnostics.Select(d=>d.Code)));
  var results=await RequestedAnalysis.ExecuteAsync(plan,new(Environment.GetEnvironmentVariable("SPICE_CORPUS_ENGINE")!));
  Assert.Equal(cards.Length,results.Length);Assert.All(results,r=>Assert.True(r.Success));
  var folder=Path.Combine(Environment.GetEnvironmentVariable("SPICE_REQUESTED_ARTIFACTS")!,Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
  for(var operation=0;operation<cards.Length;operation++){
   var input=Path.Combine(folder,operation+".cir");var raw=Path.Combine(folder,operation+".raw");var log=Path.Combine(folder,operation+".log");
   // Oracle-only ASCII output option changes serialization, not the requested original analysis.
   await File.WriteAllTextAsync(input,circuit+cards[operation]+"\n.options filetype=ascii\n.end\n");
   var start=new ProcessStartInfo(Environment.GetEnvironmentVariable("SPICE_CORPUS_NGSPICE")!){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
   foreach(var arg in new[]{"-b","-r",raw,"-o",log,input})start.ArgumentList.Add(arg);
   using var process=Process.Start(start)!;var stdout=process.StandardOutput.ReadToEndAsync();var stderr=process.StandardError.ReadToEndAsync();
   using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(30));try{await process.WaitForExitAsync(deadline.Token);}catch{if(!process.HasExited)process.Kill(true);throw;}
   await Task.WhenAll(stdout,stderr);Assert.Equal(0,process.ExitCode);
   var lines=await File.ReadAllLinesAsync(raw);var n=int.Parse(lines.Single(l=>l.StartsWith("No. Variables:",StringComparison.Ordinal)).Split(':')[1],CultureInfo.InvariantCulture);
   var names=Array.IndexOf(lines,"Variables:")+1;var output=Enumerable.Range(0,n).Single(i=>lines[names+i].Split((char[]?)null,StringSplitOptions.RemoveEmptyEntries)[1]=="v(out)");
   var first=Array.IndexOf(lines,"Values:")+1;var count=int.Parse(lines.Single(l=>l.StartsWith("No. Points:",StringComparison.Ordinal)).Split(':')[1],CultureInfo.InvariantCulture);
   double Read(int point,int variable){var parts=lines[first+point*n+variable].Split((char[]?)null,StringSplitOptions.RemoveEmptyEntries);return double.Parse(parts[^1].Split(',')[0],CultureInfo.InvariantCulture);}
   var requested=plan.Operations[operation];var actual=results[operation];
   if(requested.Kind=="dc"){
    Assert.Equal(requested.Coordinates.Length,count);
    for(var point=0;point<count;point++){Assert.Equal(requested.Coordinates[point],Read(point,0),10);Assert.Equal(Read(point,output),actual.Points[point].Response!.FinalValue("v(n2)"),10);}
   }else{
    var manifest=actual.Points[0].Response!.ReadManifest();var index=Array.FindIndex(manifest.Signals,s=>s.Id=="v(n2)");Assert.True(index>=0);
    if(requested.Kind=="ac"){
     Assert.Equal(requested.Coordinates.Length,count);Assert.Equal(count,manifest.AcSamples.Length);
     for(var point=0;point<count;point++){Assert.Equal(requested.Coordinates[point],Read(point,0),7);Assert.Equal(Read(point,output),manifest.AcSamples[point].Values[index].Real,9);Assert.Equal(0,manifest.AcSamples[point].Values[index].Imag,9);}
    }else if(requested.Kind=="op")Assert.Equal(Read(0,output),actual.Points[0].Response!.FinalValue("v(n2)"),10);
    else{
     // Independent piecewise-linear oracle interpolation on the engine's distinct internal time grid.
     foreach(var sample in manifest.Samples){var t=sample.TimeS;var hi=Enumerable.Range(0,count).FirstOrDefault(i=>Read(i,0)>=t,count-1);var lo=Math.Max(0,hi-1);var t0=Read(lo,0);var t1=Read(hi,0);var expected=t1==t0?Read(lo,output):Read(lo,output)+(Read(hi,output)-Read(lo,output))*(t-t0)/(t1-t0);Assert.InRange(Math.Abs(expected-sample.Values[index]),0,1e-9);}
    }
   }
  }
 }
 [RequestedNativeFact]
 public async Task An_engine_point_failure_withholds_partial_payloads_and_later_operations_still_execute()
 {
  var syntax=DeckParser.Parse("atomic\nV1 a 0 1\nR1 a 0 1k\n.dc V1 0 1 1\n.op\n.end\n",collectDeclarations:true);
  var plan=RequestedAnalysis.Prepare(syntax);Assert.True(plan.Success);
  var operation=plan.Operations[0];var request=operation.Requests[1];var invalid=request.Circuit.Elements.Select(e=>e is Resistor r?r with{Ohms=-1}:e).ToArray();
  operation=operation with{Requests=operation.Requests.SetItem(1,request with{Circuit=request.Circuit with{Elements=invalid}})};
  plan=plan with{Operations=plan.Operations.SetItem(0,operation)};
  var result=await RequestedAnalysis.ExecuteAsync(plan,new(Environment.GetEnvironmentVariable("SPICE_CORPUS_ENGINE")!));
  Assert.False(result[0].Success);Assert.Equal(2,result[0].Points.Length);Assert.Null(result[0].Points[0].Response);Assert.Equal("operation_failed_atomically",result[0].Points[0].TransportError);
  Assert.False(result[0].Points[1].Response!.Succeeded);Assert.True(result[1].Success);Assert.Single(result[1].Points);
 }
}