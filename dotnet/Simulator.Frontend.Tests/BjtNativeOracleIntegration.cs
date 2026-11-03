using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Simulator.Client;
using Simulator.Syntax;
using Xunit;
namespace Simulator.Frontend.Tests;
/// <summary>Owned BJT reference decks, preserving all nonzero junction and transport storage.</summary>
public sealed class BjtNativeOracleIntegration
{
 [BjtOracleTheory][InlineData("lu")][InlineData("faer")]
 public async Task Generated_Ebers_Moll_NPN_and_PNP_match_independent_native_reference(string backend)
 {
  var engine=Environment.GetEnvironmentVariable("SPICE_SEMICONDUCTOR_ENGINE")!;var ng=Environment.GetEnvironmentVariable("SPICE_NGSPICE")!;
  var parent=Environment.GetEnvironmentVariable("SPICE_SEMICONDUCTOR_ARTIFACT_ROOT")!;Assert.True(Directory.Exists(parent));
  var folder=Path.Combine(parent,"bjt-oracle-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
  foreach(var family in new[]{"NPN","PNP"})
  {
   var sign=family=="NPN"?1:-1;var phase=sign==1?0:180;var prefix=Path.Combine(folder,family).Replace('\\','/');
   var deck=$"BJT reference\nVDRV input 0 DC {(sign*.6).ToString("R",CultureInfo.InvariantCulture)} AC 1 {phase}\nVCC supply 0 DC {sign} AC 0\nRB input b 1k\nRC supply c 1k\nQ1 c b 0 qm\n.model qm {family}(IS=1f BF=100 BR=2 NF=1 NR=1 CJE=2p VJE=.75 MJE=.33 CJC=3p VJC=.75 MJC=.33 FC=.5 TF=50n TR=200n TNOM=27 LEVEL=1)\n";
   var reference=deck+$".options gmin=0 method=gear maxord=1 reltol=1e-9 abstol=1e-15 vntol=1e-12\n.ic v(b)=0 v(c)=0\n.control\nset wr_singlescale\nset wr_vecnames\nop\nwrdata {prefix}-op.txt v(b) v(c) i(VCC)\nac lin 1 10000000 10000000\nwrdata {prefix}-ac.txt v(b) v(c) i(VCC)\nreset\ntran 10p 100n 0 10p uic\nwrdata {prefix}-tran.txt v(b) v(c) i(VCC)\nquit\n.endc\n.end\n";
   var file=prefix+".cir";File.WriteAllText(file,reference);await Oracle(file,prefix+".log");
   var op=Read(prefix+"-op.txt");var ac=Read(prefix+"-ac.txt");var tr=Read(prefix+"-tran.txt");Assert.True(tr.Length>1000);
   // Each provider is a separate theory case so one provider failure cannot hide the other family.
   {
    var prepared=OperatingPointSelection.Select(DeckParser.Parse(deck+".end\n",collectDeclarations:true),backend:backend).Prepared;
    Assert.True(prepared.Success,string.Join(',',prepared.Diagnostics.Select(d=>d.Code)));
    var q=prepared.Origins.Single(o=>o.LocalName=="Q1").WireId;double? coarseError=null;
    foreach(var analysis in new Analysis[]{new OperatingPoint(),new Ac([1e7],prepared.AcSources.ToArray()),new Transient(new(1e-7,5e-11,2001)),new Transient(new(1e-7,1e-11,10001))})
    {
     var request=prepared.Request! with {Analysis=analysis};var label=analysis.GetType().Name+(analysis is Transient tx?"-"+tx.Options.StepS.ToString("R",CultureInfo.InvariantCulture):"");
     File.WriteAllText(prefix+$"-{backend}-{label}.request.json",JsonSerializer.Serialize(request,WireFormat.Options));
     using var limit=new CancellationTokenSource(TimeSpan.FromSeconds(60));var response=await new SimulationClient(engine).RunAsync(request,limit.Token);
     Assert.True(response.Succeeded,response.Payload.GetRawText());var r=response.ReadManifest();File.WriteAllText(prefix+$"-{backend}-{label}.result.json",response.Payload.GetRawText());
     var b=Array.FindIndex(r.Signals,s=>s.Id=="v("+prepared.RootNodes["b"]+")");var c=Array.FindIndex(r.Signals,s=>s.Id=="v("+prepared.RootNodes["c"]+")");var ic=Array.FindIndex(r.Signals,s=>s.Id=="i("+q+")");Assert.True(b>=0&&c>=0&&ic>=0);
     if(analysis is OperatingPoint){Check(r.Samples[0].Values[b],op[0][1],2e-7);Check(r.Samples[0].Values[c],op[0][2],2e-7);Check(r.Samples[0].Values[ic],-op[0][3],2e-10);}
     if(analysis is Ac){var a=r.AcSamples[0].Values;Check(a[b].Real,ac[0][1],2e-5);Check(a[b].Imag,ac[0][2],2e-5);Check(a[c].Real,ac[0][3],2e-5);Check(a[c].Imag,ac[0][4],2e-5);Check(a[ic].Real,-ac[0][5],2e-8);Check(a[ic].Imag,-ac[0][6],2e-8);}
     if(analysis is Transient transient&&transient.Options.StepS>1e-11){var at=r.Samples.MinBy(s=>Math.Abs(s.TimeS-2e-9))!;var expected=tr.MinBy(s=>Math.Abs(s[0]-at.TimeS))!;coarseError=Math.Abs(at.Values[c]-expected[2]);Assert.True(double.IsFinite(coarseError.Value));continue;}
     if(analysis is Transient)foreach(var time in new[]{2e-9,1e-8,5e-8,1e-7}){var actual=r.Samples.MinBy(s=>Math.Abs(s.TimeS-time))!;var expected=tr.MinBy(s=>Math.Abs(s[0]-actual.TimeS))!;if(time==2e-9){Assert.NotNull(coarseError);Assert.True(Math.Abs(actual.Values[c]-expected[2])<coarseError.Value/3,"BE refinement must reduce early collector-voltage error");}Check(actual.Values[b],expected[1],.003);Check(actual.Values[c],expected[2],.003);Check(actual.Values[ic],-expected[3],2e-6);}
    }
   }
  }
  async Task Oracle(string file,string log){var start=new ProcessStartInfo(ng){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};foreach(var a in new[]{"-b","-o",log,file})start.ArgumentList.Add(a);using var child=Process.Start(start)!;var stdout=child.StandardOutput.ReadToEndAsync();var stderr=child.StandardError.ReadToEndAsync();using var limit=new CancellationTokenSource(TimeSpan.FromSeconds(60));try{await child.WaitForExitAsync(limit.Token);}catch(OperationCanceledException){child.Kill(true);await child.WaitForExitAsync();throw;}File.WriteAllText(log+".stdout",await stdout);File.WriteAllText(log+".stderr",await stderr);Assert.Equal(0,child.ExitCode);}
  static double[][] Read(string file)=>File.ReadAllLines(file).Skip(1).Where(s=>!string.IsNullOrWhiteSpace(s)).Select(s=>s.Split((char[]?)null,StringSplitOptions.RemoveEmptyEntries).Select(s=>double.Parse(s,CultureInfo.InvariantCulture)).ToArray()).ToArray();
  static void Check(double actual,double expected,double bound)=>Assert.True(Math.Abs(actual-expected)<=bound,$"{actual:R} vs {expected:R}, bound {bound:R}");
 }
}
public sealed class BjtOracleTheoryAttribute:TheoryAttribute
{
 public BjtOracleTheoryAttribute(){if(!File.Exists(Environment.GetEnvironmentVariable("SPICE_SEMICONDUCTOR_ENGINE"))||!File.Exists(Environment.GetEnvironmentVariable("SPICE_NGSPICE")))Skip="requires existing semiconductor engine and ngspice";}
}
