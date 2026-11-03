using Simulator.Client;
using Simulator.Syntax;
using Xunit;
namespace Simulator.Frontend.Tests;
/// <summary>Independent Ebers-Moll forward/saturated collector and base currents
/// through the actual generated source-controlled native primitive assembly.</summary>
public sealed class BjtDcNativeIntegration
{
 [SemiconductorNativeFact]
 public async Task Signed_collector_and_base_currents_match_independent_transport_equations()
 {
  var engine=Environment.GetEnvironmentVariable("SPICE_SEMICONDUCTOR_ENGINE")!;
  foreach(var backend in new[]{"lu","faer"})foreach(var family in new[]{"NPN","PNP"})foreach(var vc in new[]{1.0,.2})
  {
   var sign=family=="NPN"?1:-1;
   var deck=FormattableString.Invariant($"BJT\nVC c 0 {sign*vc:R}\nVB b 0 {sign*.6:R}\nQ1 c b 0 qm\n.model qm {family}(IS=1f BF=100 BR=2 CJE=2p CJC=3p TF=50n TR=200n)\n.end\n");
   var prepared=OperatingPointSelection.Select(DeckParser.Parse(deck,collectDeclarations:true),backend:backend).Prepared;
   Assert.True(prepared.Success,string.Join(',',prepared.Diagnostics.Select(d=>d.Code)));
   using var limit=new CancellationTokenSource(TimeSpan.FromSeconds(30));var response=await new SimulationClient(engine).RunAsync(prepared.Request!,limit.Token);Assert.True(response.Succeeded,response.Payload.GetRawText());var result=response.ReadManifest();
   var collector=Array.FindIndex(result.Signals,s=>s.Id=="i("+prepared.Origins.Single(o=>o.LocalName=="Q1").WireId+")");
   var baseSource=Array.FindIndex(result.Signals,s=>s.Id=="i("+prepared.Origins.Single(o=>o.LocalName=="VB").WireId+")");
   const double vt=1.38064852e-23*300.15/1.6021766208e-19;
   var forward=1e-15*(Math.Exp(.6/vt)-1);var reverse=1e-15*(Math.Exp((.6-vc)/vt)-1);
   var expectedCollector=sign*(forward-1.5*reverse);var expectedBase=sign*(forward/100+reverse/2);
   Assert.InRange(Math.Abs(result.Samples[0].Values[collector]-expectedCollector),0,2e-12);
   Assert.InRange(Math.Abs(result.Samples[0].Values[baseSource]+expectedBase),0,2e-12);
  }
 }
}
