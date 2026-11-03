using System.Text.Json;
using Simulator.Client;
using Simulator.Syntax;
using Xunit;
namespace Simulator.Frontend.Tests;
/// <summary>Actual generated model binding and source-generated client payloads through native CPU analyses.</summary>
public sealed class SemiconductorNativeIntegration
{
 [SemiconductorNativeFact]
 public async Task Generated_diode_runs_DC_AC_and_nonzero_charge_BE_on_both_providers()
 {
  var engine=Environment.GetEnvironmentVariable("SPICE_SEMICONDUCTOR_ENGINE")!;
  var artifact=Environment.GetEnvironmentVariable("SPICE_SEMICONDUCTOR_ARTIFACT_ROOT")!;
  Assert.True(Directory.Exists(artifact));
  var syntax=DeckParser.Parse("Junction\nV1 in 0 DC .5 AC 1\nR1 in out 1k\nD1 out 0 dm\n.model dm D(IS=1e-14 N=1 CJO=2p VJ=.8 M=.5 FC=.5 TT=1n)\n.end\n",collectDeclarations:true);
  foreach(var backend in new[]{"lu","faer"})
  {
   var prepared=OperatingPointSelection.Select(syntax,backend:backend).Prepared;
   Assert.True(prepared.Success,string.Join(',',prepared.Diagnostics.Select(d=>d.Code)));
   var id=prepared.Origins.Single(o=>o.LocalName=="D1").WireId;
   foreach(var analysis in new Analysis[]{new OperatingPoint(),new Ac([1e6],prepared.AcSources.ToArray()),new Transient(new(1e-9,1e-10,10))})
   {
    var request=prepared.Request! with {Analysis=analysis};
    var label=analysis.GetType().Name;
    File.WriteAllText(Path.Combine(artifact,$"frontend-{backend}-{label}.request.json"),JsonSerializer.Serialize(request,WireFormat.Options));
    using var limit=new CancellationTokenSource(TimeSpan.FromSeconds(30));
    var response=await new SimulationClient(engine).RunAsync(request,limit.Token);Assert.True(response.Succeeded,response.Payload.GetRawText());
    var result=response.ReadManifest();
    File.WriteAllText(Path.Combine(artifact,$"frontend-{backend}-{label}.result.json"),response.Payload.GetRawText());
    var output=Array.FindIndex(result.Signals,s=>s.Id=="v("+prepared.RootNodes["out"]+")");
    var input=Array.FindIndex(result.Signals,s=>s.Id=="v("+prepared.RootNodes["in"]+")");
    var total=Array.FindIndex(result.Signals,s=>s.Id=="i("+id+")");
    Assert.True(output>=0&&input>=0&&total>=0);Assert.Contains(result.Signals,s=>s.Id=="i_conduction("+id+")");
    foreach(var sample in result.Samples)Assert.InRange(Math.Abs(sample.Values[total]-(sample.Values[input]-sample.Values[output])/1000),0,1e-10);
    foreach(var sample in result.AcSamples){Assert.InRange(Math.Abs(sample.Values[total].Real-(sample.Values[input].Real-sample.Values[output].Real)/1000),0,1e-10);Assert.InRange(Math.Abs(sample.Values[total].Imag-(sample.Values[input].Imag-sample.Values[output].Imag)/1000),0,1e-10);}
   }
  }
 }
}
/// <summary>The external engine is invoked only when the campaign explicitly supplies its path.</summary>
public sealed class SemiconductorNativeFactAttribute:FactAttribute
{
 public SemiconductorNativeFactAttribute(){if(!File.Exists(Environment.GetEnvironmentVariable("SPICE_SEMICONDUCTOR_ENGINE")))Skip="requires existing SPICE_SEMICONDUCTOR_ENGINE";}
}
