using Simulator.Syntax;
using Simulator.Client;
using Xunit;
namespace Simulator.Frontend.Tests;
public sealed class Mos1Tests {
 [Theory][InlineData("NMOS")][InlineData("PMOS")]
 public void Qualified_mos1_body_and_overlap_profile_lowers(string family) {
  var result=OperatingPointSelection.Select(DeckParser.Parse("MOS\nVd d 0 1\nVg g 0 1.5\nM1 d g 0 0 mm L=1u W=10u\n.model mm "+family+"(LEVEL=1 VTO=.7 KP=20u GAMMA=.4 PHI=.6 LAMBDA=.03 CBD=2p CBS=3p CGSO=2e-7 CGDO=3e-7 CGBO=1e-6)\n.end\n",collectDeclarations:true)).Prepared;
  Assert.True(result.Success,string.Join(',',result.Diagnostics.Select(d=>d.Code)));
  Assert.Contains(result.Origins,o=>o.LocalName=="M1");
  var channel=Assert.Single(result.Request!.Circuit.Elements.OfType<Mos1Channel>());
  Assert.Equal(family=="PMOS",channel.Model.PChannel);Assert.Equal(.7,channel.Model.ThresholdVolts);
  Assert.Equal(2e-4,channel.Model.BetaAmpsPerVoltSquared,14);Assert.Equal(.4,channel.Model.BodyEffectSqrtVolts);
  Assert.Equal(.6,channel.Model.SurfacePotentialVolts);Assert.Equal(.03,channel.Model.ChannelLengthModulationPerVolt);
  var body=Assert.Single(result.Request.Circuit.Elements.OfType<SpiceDiode>());Assert.Equal(2e-12,body.Model.JunctionFarads);
  Assert.Equal(.8,body.Model.JunctionVolts);Assert.Equal(.5,body.Model.Grading);Assert.Equal(.5,body.Model.ForwardCoefficient);
  var caps=result.Request.Circuit.Elements.OfType<Capacitor>().ToArray();Assert.Equal(3,caps.Length);
  Assert.Contains(caps,c=>Math.Abs(c.Farads-2e-12)<1e-25);Assert.Contains(caps,c=>Math.Abs(c.Farads-3e-12)<1e-25);Assert.Contains(caps,c=>Math.Abs(c.Farads-1e-12)<1e-25);
  var sensor=result.Request.Circuit.Elements.OfType<Voltage>().Single(v=>v.Id==result.Origins.Single(o=>o.LocalName=="M1").WireId);
  Assert.Equal(sensor.N,channel.P);Assert.Equal(family=="PMOS"?sensor.N:0,body.P);Assert.Equal(family=="PMOS"?0:sensor.N,body.N);
 }
 [Theory][InlineData("TOX=10n")][InlineData("LEVEL=2")][InlineData("UO=600")][InlineData("KP=0")][InlineData("VTO=.7 VTO=.8")][InlineData("MJ=1")][InlineData("CBD=-1p")][InlineData("TNOM=28")][InlineData("KP=1e308")]
 public void Unsupported_profile_refuses_atomically(string terms) {
  var result=OperatingPointSelection.Select(DeckParser.Parse("MOS\nVd d 0 1\nVg g 0 1.5\nM1 d g 0 0 mm L=1u W=10u\n.model mm NMOS("+terms+")\n.end\n",collectDeclarations:true)).Prepared;
  Assert.False(result.Success);Assert.Null(result.Request);Assert.Empty(result.Origins);
 }
}
