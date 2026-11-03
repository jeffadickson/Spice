using Simulator.Syntax;
using Xunit;
namespace Simulator.Frontend.Tests;
public sealed class PspiceSwitchTests
{
 [Fact]
 public void Bound_switch_uses_instance_parameters_and_formal_ports()
 {
  var deck="Switch\nV1 in 0 DC .1 AC 1\nX1 in out cell on=100\n.subckt cell p q on=200\nVctrl c 0 .1\nS1 p q c 0 mode\n.model mode VSWITCH(RON={on} ROFF=10k VON=.2 VOFF=0)\nR1 q 0 1k\n.ends\n.end\n";
  var syntax=DeckParser.Parse(deck,collectDeclarations:true);
  var result=OperatingPointSelection.Select(syntax);
  Assert.True(result.Success,string.Join(',',result.Prepared.Diagnostics.Select(d=>d.Code)));
  Assert.False(OperatingPointBridge.Lower(syntax).Success);
  var s=Assert.Single(result.Prepared.Request!.Circuit.Elements.OfType<Simulator.Client.PspiceSwitch>());
  Assert.Equal(100,s.Ron);Assert.Equal(10000,s.Roff);Assert.NotEqual(s.P,s.N);
  Assert.DoesNotContain(CircuitReadiness.Audit(syntax).Issues,i=>i.Kind=="switchCard");
 }
 [Theory]
 [InlineData("VSWITCH(RON=-1 ROFF=10k VON=.2 VOFF=0)")]
 [InlineData("VSWITCH(RON=100 ROFF=10k VON=0 VOFF=0)")]
 [InlineData("VSWITCH(RON=100 ROFF=10k VON=.2 VOFF=0 EXTRA=1)")]
 [InlineData("VSWITCH(RON=100 ROFF=10k VON=.2)")]
 [InlineData("VSWITCH(RON=100 ROFF=10k VON=.2 VOFF=0 FLAG)")]
 [InlineData("VSWITCH(RON=100 ROFF=10k VON=.2 VOFF=0 RON=200)")]
 [InlineData("SW(RON=100 ROFF=10k VT=.1 VH=0)")]
 public void Unsupported_models_refuse_without_partial_state(string model)
 {
  var r=OperatingPointSelection.Select(DeckParser.Parse("Switch\nV1 a 0 1\nS1 a b c 0 mode\n.model mode "+model+"\n.end\n",collectDeclarations:true)).Prepared;
  Assert.False(r.Success);Assert.Null(r.Request);Assert.Empty(r.Origins);Assert.Empty(r.AcSources);
  if(model.Contains("EXTRA",StringComparison.Ordinal)||model.Contains("FLAG",StringComparison.Ordinal))Assert.Contains(CircuitReadiness.Audit(DeckParser.Parse("Switch\nV1 a 0 1\nS1 a b c 0 mode\n.model mode "+model+"\n.end\n",collectDeclarations:true)).Issues,i=>i.Code=="unsupported_switch_profile");
 }
 [Fact]
 public void Model_expression_resolves_in_its_declaration_scope()
 {
  var deck="Scope\n.param r=100\n.model mode VSWITCH(RON={r} ROFF=10k VON=.2 VOFF=0)\nV1 a 0 .1\nX1 a b cell\n.subckt cell p q\n.param r=1000\nVc c 0 .1\nS1 p q c 0 mode\nR1 q 0 1k\n.ends\n.end\n";
  var result=OperatingPointSelection.Select(DeckParser.Parse(deck,collectDeclarations:true)).Prepared;
  Assert.True(result.Success,string.Join(',',result.Diagnostics.Select(d=>d.Code)));
  Assert.Equal(100,Assert.Single(result.Request!.Circuit.Elements.OfType<Simulator.Client.PspiceSwitch>()).Ron);
 }
}
