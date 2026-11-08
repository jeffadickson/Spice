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
 [Theory]
 [InlineData("", "", 2e-5)]
 [InlineData("", "L=50u", 4e-5)]
 [InlineData("", "W=50u", 1e-5)]
 [InlineData("L=3u W=9u", "", 6e-5)]
 [InlineData("L=3u W=9u", "L=5u W=10u", 4e-5)]
 [InlineData("L=3u W=9u", "W=6u", 4e-5)]
 public void Geometry_defaults_resolve_independently(string modelTerms,string instanceTerms,double beta) {
  var deck=$"MOS geometry\nVd d 0 1\nVg g 0 1.5\nM1 d g 0 0 mm {instanceTerms}\n.model mm NMOS(LEVEL=1 {modelTerms})\n.end\n";
  var result=OperatingPointSelection.Select(DeckParser.Parse(deck,collectDeclarations:true)).Prepared;
  Assert.True(result.Success,string.Join(',',result.Diagnostics.Select(d=>d.Code)));
  var channel=Assert.Single(result.Request!.Circuit.Elements.OfType<Mos1Channel>());
  Assert.Equal(beta,channel.Model.BetaAmpsPerVoltSquared,14);
 }
 [Theory][InlineData("L=0")][InlineData("W=-1u")][InlineData("L=2u L=3u")][InlineData("M=2")]
 public void Invalid_instance_geometry_refuses_atomically(string terms) {
  var deck=$"MOS geometry\nVd d 0 1\nVg g 0 1.5\nM1 d g 0 0 mm {terms}\n.model mm NMOS(LEVEL=1 L=3u W=9u)\n.end\n";
  var result=OperatingPointSelection.Select(DeckParser.Parse(deck,collectDeclarations:true)).Prepared;
  Assert.False(result.Success);Assert.Null(result.Request);Assert.Empty(result.Origins);
 }
 [Fact]
 public void Series_resistances_connect_to_the_intrinsic_mos_terminals() {
  var deck="MOS resistance\nVd d 0 1\nVg g 0 2\nM1 d g 0 0 mm\n.model mm NMOS(RD=20 RS=10 CBD=1p CBS=1p)\n.end\n";
  var result=OperatingPointSelection.Select(DeckParser.Parse(deck,collectDeclarations:true)).Prepared;
  Assert.True(result.Success,string.Join(',',result.Diagnostics.Select(d=>d.Code)));
  var elements=result.Request!.Circuit.Elements;
  var channel=Assert.Single(elements.OfType<Mos1Channel>());
  var sensor=Assert.Single(elements.OfType<Voltage>(),v=>v.Id==result.Origins.Single(o=>o.LocalName=="M1").WireId);
  Assert.Contains(elements.OfType<Resistor>(),r=>r.Ohms==20&&r.P==sensor.N&&r.N==channel.P);
  Assert.Contains(elements.OfType<Resistor>(),r=>r.Ohms==10&&r.P==0&&r.N==channel.N);
  Assert.Contains(elements.OfType<SpiceDiode>(),v=>v.N==channel.N&&v.Model.JunctionFarads==1e-12);
 }
 [Theory][InlineData("TOX=10n")][InlineData("RD=-1")][InlineData("RS=-1")][InlineData("LEVEL=2")][InlineData("UO=600")][InlineData("KP=0")][InlineData("VTO=.7 VTO=.8")][InlineData("MJ=1")][InlineData("CBD=-1p")][InlineData("TNOM=28")][InlineData("KP=1e308")]
 public void Unsupported_profile_refuses_atomically(string terms) {
  var result=OperatingPointSelection.Select(DeckParser.Parse("MOS\nVd d 0 1\nVg g 0 1.5\nM1 d g 0 0 mm L=1u W=10u\n.model mm NMOS("+terms+")\n.end\n",collectDeclarations:true)).Prepared;
  Assert.False(result.Success);Assert.Null(result.Request);Assert.Empty(result.Origins);
 }
}
