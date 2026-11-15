using Simulator.Client;
using Simulator.Frontend;
using Simulator.Syntax;
using Xunit;
namespace Simulator.Frontend.Tests;
public sealed class MutualInductanceTests {
 [Fact] public void Generated_k_card_resolves_hierarchical_windings_after_expansion() {
  var plan=RequestedAnalysis.Prepare(DeckParser.Parse("Coupling\nV1 in 0 1\nR1 in a 10\nX1 a b wind\nR2 b 0 40\nK1 X1:L1 X1:L2 -.75\n.subckt wind p q\nL1 p 0 1m\nL2 q 0 4m\n.ends\n.ac lin 2 100 1000\n.end\n",collectDeclarations:true));
  Assert.True(plan.Success,string.Join(',',plan.Diagnostics.Select(d=>d.Code)));
  var c=plan.Operations[0].Requests[0].Circuit;var k=Assert.Single(c.Elements.OfType<MutualInductance>());
  Assert.Equal(-.75,k.Coefficient);Assert.Contains(c.Elements,e=>e.Id==k.First&&e is Inductor);Assert.Contains(c.Elements,e=>e.Id==k.Second&&e is Inductor);
 }
 [Theory][InlineData("K1 L1 missing .5","missing_coupled_inductor")][InlineData("K1 L1 L1 .5","invalid_coupled_windings")][InlineData("K1 L1 L2 1.1","invalid_mutual_coupling")][InlineData("K1 L1 L2 .5 nonlinear","unsupported_active_card")]
 public void Invalid_coupling_publishes_no_partial_plan(string card,string diagnostic){
  var p=RequestedAnalysis.Prepare(DeckParser.Parse($"Coupling\nV1 in 0 1\nR1 in a 10\nL1 a 0 1m\nL2 b 0 4m\nR2 b 0 40\n{card}\n.op\n.end\n",collectDeclarations:true));
  Assert.False(p.Success);Assert.Empty(p.Operations);Assert.Contains(p.Diagnostics,d=>d.Code==diagnostic);
 }
 [Fact] public void Multiwinding_generated_card_expands_all_pairs(){
  var p=RequestedAnalysis.Prepare(DeckParser.Parse("Coupling\nV1 in 0 1\nR1 in a 10\nL1 a 0 1m\nL2 b 0 4m\nL3 c 0 9m\nR2 b 0 40\nR3 c 0 90\nK1 L1 L2 L3 .5\n.op\n.end\n",collectDeclarations:true));Assert.True(p.Success,string.Join(',',p.Diagnostics.Select(d=>d.Code)));Assert.Equal(3,p.Operations[0].Requests[0].Circuit.Elements.OfType<MutualInductance>().Count());
 }
 [Fact] public void Uic_hierarchical_current_hints_drive_the_consistency_solve(){
  var p=RequestedAnalysis.Prepare(DeckParser.Parse("Initial winding current\nR1 a 0 1\nR2 b 0 2\nX1 a b winding params: amps=5\n.subckt winding p q params: amps=0\nL1 p 0 10m IC={amps}\nL2 q 0 40m IC=-2\nK1 L1 L2 -.75\n.ends\n.tran .1m 50m UIC\n.end\n",collectDeclarations:true));
  Assert.True(p.Success,string.Join(',',p.Diagnostics.Select(d=>d.Code)));var op=Assert.Single(p.Operations);Assert.True(op.UsesUic);
  Assert.Equal(new[]{-2d,5d},op.InductorInitialCurrents.Values.Order().ToArray());var bias=Assert.IsType<SimulationRequest>(op.InitializationRequest);
  Assert.Empty(bias.Circuit.Elements.OfType<MutualInductance>());Assert.Empty(bias.Circuit.Elements.OfType<Inductor>());Assert.Equal(new[]{-2d,5d},bias.Circuit.Elements.OfType<Current>().Select(c=>c.Amps).Order().ToArray());
 }
 [Fact]public void Ordinary_transient_retains_but_does_not_apply_device_current_hint(){
  var p=RequestedAnalysis.Prepare(DeckParser.Parse("Initial winding current\nV1 in 0 1\nR1 in a 1\nL1 a 0 10m IC=5\n.tran .1m 1m\n.end\n",collectDeclarations:true));Assert.True(p.Success);var op=Assert.Single(p.Operations);Assert.False(op.UsesUic);Assert.Null(op.InitializationRequest);Assert.Equal(5,Assert.Single(op.InductorInitialCurrents).Value);
 }}
