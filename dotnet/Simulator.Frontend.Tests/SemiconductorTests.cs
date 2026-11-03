using Simulator.Syntax;
using Simulator.Client;
using Xunit;
namespace Simulator.Frontend.Tests;
public sealed class SemiconductorTests {
 [Theory][InlineData("CJO")][InlineData("CJ0")]
 public void Qualified_parameters_are_visible_to_readiness_and_zero_charge_boundaries_work(string alias) {
  var syntax=DeckParser.Parse("Allowed\nV1 a 0 .5\nD1 a 0 dm\n.model dm D(IS=1e-14 N=2 "+alias+"=0 VJ=.8 M=0 FC=0 TT=0 TNOM=27)\n.end\n",collectDeclarations:true);
  var audit=CircuitReadiness.Audit(syntax);Assert.True(audit.CanPrepare);Assert.DoesNotContain(audit.Issues,i=>i.Kind=="diode");
  var d=Assert.Single(OperatingPointSelection.Select(syntax).Prepared.Request!.Circuit.Elements.OfType<SpiceDiode>());
  Assert.Equal(2*1.38064852e-23*300.15/1.6021766208e-19,d.Model.ThermalVolts,14);
  Assert.Equal(0,d.Model.JunctionFarads);Assert.Equal(0,d.Model.Grading);Assert.Equal(0,d.Model.ForwardCoefficient);Assert.Equal(0,d.Model.TransitSeconds);
 }
 [Fact] public void Underflowed_thermal_voltage_is_refused() {Assert.False(Select("D(N=5e-324)").Success);}
 [Fact] public void Unsupported_assignments_remain_visible_behind_an_earlier_directive() {
  var syntax=DeckParser.Parse("Multiple\n.options reltol=1m\nV1 a 0 .5\nD1 a 0 dm\n.model dm D(RS=1)\n.end\n",collectDeclarations:true);
  var audit=CircuitReadiness.Audit(syntax);Assert.Contains(audit.Issues,i=>i.Layer=="directive_policy");Assert.Contains(audit.Issues,i=>i.Code=="unsupported_diode_profile");
 }
 [Fact] public void Model_values_use_declaration_scope_inside_nested_instances() {
  var syntax=DeckParser.Parse("Scope\n.param c=2p\n.model dm D(CJO={c})\nV1 a 0 .5\nX1 a 0 cell\n.subckt cell p n\n.param c=5p\nD1 p n dm\n.ends\n.end\n",collectDeclarations:true);
  var result=OperatingPointSelection.Select(syntax).Prepared;Assert.True(result.Success,string.Join(',',result.Diagnostics.Select(d=>d.Code)));
  Assert.Equal(2e-12,Assert.Single(result.Request!.Circuit.Elements.OfType<SpiceDiode>()).Model.JunctionFarads);
 }
 [Fact] public void Nonzero_depletion_and_transport_parameters_are_lowered() {var r=Select("D(IS=1e-14 N=1 CJO=2p VJ=.8 M=.5 FC=.5 TT=1n)");Assert.True(r.Success,string.Join(',',r.Diagnostics.Select(d=>d.Code)));var d=Assert.Single(r.Request!.Circuit.Elements.OfType<SpiceDiode>());Assert.Equal(2e-12,d.Model.JunctionFarads);Assert.Equal(1e-9,d.Model.TransitSeconds);}
 [Theory][InlineData("D(IS=1e-14 TYPO=1)")][InlineData("D(IS=1e-14 RS=1)")][InlineData("D(M=1)")][InlineData("D(FC=1)")][InlineData("D(TT=-1n)")][InlineData("D(IS=1e-14 IS=2e-14)")][InlineData("D(BV=10)")][InlineData("D(IS={missing})")][InlineData("D(FLAG)")][InlineData("D(TNOM=30)")][InlineData("D(IS=0)")][InlineData("D(N=0)")][InlineData("D(VJ=0)")][InlineData("D(CJO=-1p)")][InlineData("D(CJO=1p CJ0=2p)")]public void Unsupported_or_invalid_parameters_refuse_atomically(string model){var r=Select(model);Assert.False(r.Success);Assert.Null(r.Request);Assert.Empty(r.Origins);Assert.All(r.Diagnostics,d=>Assert.False(string.IsNullOrWhiteSpace(d.Code)));}
 private static BridgeResult Select(string model)=>OperatingPointSelection.Select(DeckParser.Parse("Junction\nV1 in 0 .5\nR1 in out 1k\nD1 out 0 dm\n.model dm "+model+"\n.end\n",collectDeclarations:true)).Prepared;
}
