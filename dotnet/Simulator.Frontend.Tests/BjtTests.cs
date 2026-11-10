using Simulator.Syntax;
using Simulator.Client;
using Xunit;
namespace Simulator.Frontend.Tests;
public sealed class BjtTests
{
 [Theory][InlineData("",0)][InlineData("CJE=2p",1)][InlineData("TF=1n",1)][InlineData("CJC=3p",1)][InlineData("TR=2n",1)]
 public void Each_individual_physical_storage_term_adds_its_companion(string terms,int count) {
  var r=Select("NPN("+terms+")");Assert.True(r.Success,string.Join(',',r.Diagnostics.Select(d=>d.Code)));Assert.Equal(count,r.Request!.Circuit.Elements.OfType<JunctionStorage>().Count());
 }
 [Fact] public void Diode_connected_and_ground_emitter_topologies_are_valid() {
  var result=OperatingPointSelection.Select(DeckParser.Parse("Connected\nV1 b 0 .6\nQ1 b b 0 qm\n.model qm NPN(IS=1f BF=100 CJE=2p TF=1n)\n.end\n",collectDeclarations:true)).Prepared;
  Assert.True(result.Success,string.Join(',',result.Diagnostics.Select(d=>d.Code)));
 }
 [Theory][InlineData("NPN")][InlineData("PNP")]
 public void Bounded_BJT_separates_conduction_sensors_from_real_charge(string family)
 {
  var result=Select(family+"(IS=1f BF=100 BR=2 NF=1.2 NR=1.5 CJE=2p VJE=.8 MJE=.4 CJC=3p VJC=.9 MJC=.6 FC=.4 TF=1n TR=2n TNOM=27 LEVEL=1)");
  Assert.True(result.Success,string.Join(',',result.Diagnostics.Select(d=>d.Code)));
  var elements=result.Request!.Circuit.Elements;Assert.Equal(2,elements.OfType<Cccs>().Count());Assert.Equal(2,elements.OfType<Diode>().Count());
  var charge=elements.OfType<JunctionStorage>().ToArray();Assert.Equal(2,charge.Length);Assert.All(charge,c=>Assert.Equal(1e-15,c.Model.SaturationAmps));Assert.Contains(charge,c=>c.Model.TransitSeconds==1e-9&&c.Model.JunctionFarads==2e-12);
  var q=result.Origins.Single(o=>o.LocalName=="Q1");Assert.Contains(elements.OfType<Voltage>(),v=>v.Id==q.WireId&&v.Volts==0);
  var c=elements.OfType<Voltage>().Single(v=>v.Id==q.WireId).N;
  var b=Array.IndexOf(result.Request.Circuit.Nodes,result.RootNodes["b"]);var e=Array.IndexOf(result.Request.Circuit.Nodes,result.RootNodes["e"]);
  var forward=elements.OfType<Cccs>().Single(v=>v.Gain==100);var reverse=elements.OfType<Cccs>().Single(v=>v.Gain==2);
  var sense=elements.OfType<Voltage>().Single(v=>v.Id==forward.ControlId);
  Assert.Equal(family=="NPN"?c:e,forward.P);Assert.Equal(family=="NPN"?e:c,forward.N);
  Assert.Equal(family=="NPN"?e:c,reverse.P);Assert.Equal(family=="NPN"?c:e,reverse.N);
  var be=charge.Single(v=>v.Model.JunctionFarads==2e-12);var bc=charge.Single(v=>v.Model.JunctionFarads==3e-12);
  Assert.Equal(.8,be.Model.JunctionVolts);Assert.Equal(.4,be.Model.Grading);Assert.Equal(.4,be.Model.ForwardCoefficient);Assert.Equal(1e-9,be.Model.TransitSeconds);
  Assert.Equal(.9,bc.Model.JunctionVolts);Assert.Equal(.6,bc.Model.Grading);Assert.Equal(.4,bc.Model.ForwardCoefficient);Assert.Equal(2e-9,bc.Model.TransitSeconds);
  Assert.Equal(1.2*1.38064852e-23*300.15/1.6021766208e-19,be.Model.ThermalVolts,14);Assert.Equal(1.5*1.38064852e-23*300.15/1.6021766208e-19,bc.Model.ThermalVolts,14);
  Assert.Equal(family=="NPN"?b:e,be.P);Assert.Equal(family=="NPN"?e:b,be.N);
  Assert.Equal(family=="NPN"?b:c,bc.P);Assert.Equal(family=="NPN"?c:b,bc.N);
  Assert.NotEqual(sense.N,be.P);Assert.NotEqual(sense.N,be.N);
  Assert.Equal(elements.Length,elements.Select(v=>v.Id).Distinct(StringComparer.Ordinal).Count());
  Assert.DoesNotContain(result.RootNodes.Keys,k=>k.StartsWith("$",StringComparison.Ordinal));
 }
 [Theory][InlineData("NPN")][InlineData("PNP")]
 public void Ohmic_resistances_separate_external_and_intrinsic_bjt_terminals(string family)
 {
  var result=Select(family+"(RB=100 RC=20 RE=10 CJE=1p CJC=1p)");
  Assert.True(result.Success,string.Join(',',result.Diagnostics.Select(d=>d.Code)));
  var circuit=result.Request!.Circuit;
  var resistors=circuit.Elements.OfType<Resistor>().ToArray();
  Assert.Equal(new double[]{10,20,100},resistors.Select(r=>r.Ohms).Order());
  var rb=Assert.Single(resistors,r=>r.Ohms==100);
  var re=Assert.Single(resistors,r=>r.Ohms==10);
  var rc=Assert.Single(resistors,r=>r.Ohms==20);
  Assert.Equal(Array.IndexOf(circuit.Nodes,result.RootNodes["b"]),rb.P);
  var storage=circuit.Elements.OfType<JunctionStorage>().ToArray();
  Assert.Contains(storage,q=>family=="NPN"?q.P==rb.N&&q.N==re.N:q.P==re.N&&q.N==rb.N);
  Assert.Contains(storage,q=>family=="NPN"?q.P==rb.N&&q.N==rc.N:q.P==rc.N&&q.N==rb.N);
 }
 [Theory][InlineData("NPN(VAF=100)")][InlineData("NPN(IKF=1m)")][InlineData("NPN(RB=-1)")][InlineData("NPN(RBM=10)")][InlineData("NPN(IRB=1m)")][InlineData("NPN(RC=-1)")][InlineData("NPN(RE=-1)")][InlineData("NPN(BF=0)")][InlineData("NPN(IS=1 BF=5e-324)")][InlineData("NPN(IS=0)")][InlineData("NPN(CJE=-1p)")][InlineData("NPN(MJE=1)")][InlineData("NPN(TF=-1n)")][InlineData("NPN(TNOM=30)")][InlineData("NPN(LEVEL=4)")][InlineData("NPN(BF=100 BF=200)")]
 public void Unsupported_or_invalid_BJT_terms_refuse_atomically(string model){var r=Select(model);Assert.False(r.Success);Assert.Null(r.Request);Assert.Empty(r.Origins);}
 [Theory][InlineData("NPN","c")][InlineData("PNP","b")]
 public void Explicit_substrate_uses_default_native_leakage_reference(string family,string reference)
 {
  var result=OperatingPointSelection.Select(DeckParser.Parse("BJT substrate\nVc c 0 1\nVb b 0 .6\nVe e 0 0\nVs s 0 0\nQ1 c b e s qm\n.model qm "+family+"\n.end\n",collectDeclarations:true)).Prepared;
  Assert.True(result.Success,string.Join(',',result.Diagnostics.Select(d=>d.Code)));
  var request=result.Request!;
  var origin=Assert.Single(result.Origins,o=>o.LocalName=="Q1");
  var collector=Assert.Single(request.Circuit.Elements.OfType<Voltage>(),e=>e.Id==origin.WireId);
  var shunt=Assert.Single(request.Circuit.Elements.OfType<Resistor>());
  Assert.Equal(1e12,shunt.Ohms);
  Assert.Equal(Array.IndexOf(request.Circuit.Nodes,result.RootNodes["s"]),shunt.P);
  Assert.Equal(reference=="c"?collector.N:Array.IndexOf(request.Circuit.Nodes,result.RootNodes["b"]),shunt.N);
 }

 [Theory]
 [InlineData("NPN","CCS",true)]
 [InlineData("PNP","CJS",true)]
 [InlineData("NPN","CSUB",false)]
 [InlineData("PNP","CCS",false)]
 public void Substrate_charge_uses_intrinsic_reference_and_zero_forward_knee(string family,string alias,bool explicitPort)
 {
  var ports=explicitPort?" s":"";
  var deck=$"BJT substrate\nVc c 0 1\nVb b 0 .6\nVe e 0 0\nVs s 0 0\nQ1 c b e{ports} qm\n.model qm {family}({alias}=2p PS=.8 MS=.4 FC=.5 RB=10 RC=20)\n.end\n";
  var result=OperatingPointSelection.Select(DeckParser.Parse(deck,collectDeclarations:true)).Prepared;
  Assert.True(result.Success,string.Join(',',result.Diagnostics.Select(d=>d.Code)));
  var circuit=result.Request!.Circuit;
  var intrinsic=Assert.Single(circuit.Elements.OfType<Resistor>(),r=>r.Ohms==(family=="NPN"?20:10)).N;
  var storage=Assert.Single(circuit.Elements.OfType<JunctionStorage>());
  Assert.Equal(explicitPort?Array.IndexOf(circuit.Nodes,result.RootNodes["s"]):Array.IndexOf(circuit.Nodes,"0"),storage.P);
  Assert.Equal(intrinsic,storage.N);
  Assert.Equal(2e-12,storage.Model.JunctionFarads);
  Assert.Equal(.8,storage.Model.JunctionVolts);
  Assert.Equal(.4,storage.Model.Grading);
  Assert.Equal(0,storage.Model.ForwardCoefficient);
  Assert.Equal(0,storage.Model.TransitSeconds);
 }

 [Theory]
 [InlineData("CCS=-1p")]
 [InlineData("CJS=1p CCS=2p")]
 [InlineData("VJS=0")]
 [InlineData("MJS=1")]
 [InlineData("ISS=1f")]
 public void Invalid_or_unsupported_substrate_terms_refuse_atomically(string terms)
 {
  var result=Select($"NPN({terms})");
  Assert.False(result.Success);
  Assert.Null(result.Request);
  Assert.Empty(result.Origins);
 }

 private static BridgeResult Select(string model)=>OperatingPointSelection.Select(DeckParser.Parse("BJT\nVc c 0 1\nVb b 0 .6\nVe e 0 0\nQ1 c b e qm\n.model qm "+model+"\n.end\n",collectDeclarations:true)).Prepared;
}
