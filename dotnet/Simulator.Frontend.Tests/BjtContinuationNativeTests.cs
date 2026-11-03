using System.Text.Json;
using Simulator.Client;
using Simulator.Syntax;
using Xunit;
namespace Simulator.Frontend.Tests;

/// <summary>Composite nonlinear recovery checked against inverse Ebers–Moll, not the Newton residual implementation.</summary>
public sealed class BjtContinuationNativeTests
{
 [BjtOracleTheory]
 [InlineData("npn","lu")][InlineData("npn","faer")]
 [InlineData("pnp","lu")][InlineData("pnp","faer")]
 public async Task Diode_connected_composite_recovers_and_removes_all_artificial_shunts(string family,string backend)
 {
  var sign=family=="npn"?1:-1;var root=Environment.GetEnvironmentVariable("SPICE_SEMICONDUCTOR_ARTIFACT_ROOT")!;
  Assert.True(Directory.Exists(root));var folder=Path.Combine(root,"bjt-continuation-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
  var source=$"Composite recovery\nI1 0 b {sign}u\nQ1 b b 0 qm\n.model qm {family}(IS=1f BF=100 BR=2 CJE=2p CJC=3p TF=50n TR=200n)\n.end\n";
  var selected=OperatingPointSelection.Select(DeckParser.Parse(source,collectDeclarations:true),backend:backend);Assert.True(selected.Success);
  var client=new SimulationClient(Environment.GetEnvironmentVariable("SPICE_SEMICONDUCTOR_ENGINE")!);
  async Task<SimulationResponse> Run(string name,NewtonOptions policy,bool reverse=false)
  {
   var request=selected.Prepared.Request! with{CaseId=name,Analysis=new OperatingPointNewton(policy)};
   if(reverse)request=request with{Circuit=request.Circuit with{Elements=request.Circuit.Elements.Select(e=>e is Current current?current with{Amps=-current.Amps}:e).ToArray()}};
   await File.WriteAllTextAsync(Path.Combine(folder,name+".request.json"),JsonSerializer.Serialize(request,WireFormat.Options));
   using var limit=new CancellationTokenSource(TimeSpan.FromSeconds(30));var response=await client.RunAsync(request,limit.Token);
   await File.WriteAllTextAsync(Path.Combine(folder,name+".response.json"),response.Payload.GetRawText());return response;
  }
  var direct=await Run("direct",new(MaxIterations:8));Assert.False(direct.Succeeded);
  Assert.Equal("numerical_failure",direct.Payload.GetProperty("error").GetProperty("code").GetString());
  var coarse=await Run("coarse-source",new(MaxIterations:8,Continuation:new(.125,1e-6,128)));
  Assert.False(coarse.Succeeded);Assert.Equal("numerical_failure",coarse.Payload.GetProperty("error").GetProperty("code").GetString());
  const double vt=1.38064852e-23*300.15/1.6021766208e-19;
  // With C=B, reverse transport is exactly zero. The injected current is
  // IS*(1+1/BF)*expm1(sign*Vb/VT), including physical base current.
  var expected=sign*vt*Math.Log(1+1e-6/(1e-15*1.01));
  foreach(var (name,policy) in new[]{("source",new NewtonOptions(MaxIterations:8,Continuation:new(.125,1e-12,128))),
                                    ("gmin",new NewtonOptions(MaxIterations:8,GminContinuation:new(1,1e-9,.5,64)))})
  {
   var result=await Run(name,policy);Assert.True(result.Succeeded,result.Payload.GetRawText());
   var voltage=result.FinalValue("v("+selected.Prepared.RootNodes["b"]+")");
   Assert.InRange(Math.Abs(voltage-expected),0,2e-7);
   Assert.InRange(Math.Abs(1e-15*1.01*(Math.Exp(sign*voltage/vt)-1)-1e-6),0,2e-12);
   var impossible=await Run("impossible-"+name,policy,reverse:true);
   Assert.False(impossible.Succeeded);Assert.Equal("numerical_failure",impossible.Payload.GetProperty("error").GetProperty("code").GetString());
  }
 }
}
