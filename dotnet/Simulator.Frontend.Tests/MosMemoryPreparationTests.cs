using Simulator.Client;
using Simulator.Syntax;
using Xunit;
namespace Simulator.Frontend.Tests;
public sealed class MosMemoryPreparationTests
{
 [Theory][InlineData("NMOS")][InlineData("PMOS")]
 public void BodyJunctionsRetainTheNativeDefaultConductance(string family)
 {
  var plan=OperatingPointSelection.Select(DeckParser.Parse($"junctions\nV1 d 0 1\nV2 g 0 1\nV3 s 0 .2\nM1 d g s 0 model\n.model model {family}(LEVEL=1)\n.end\n",collectDeclarations:true)).Prepared;
  Assert.True(plan.Success,string.Join(',',plan.Diagnostics.Select(d=>d.Code)));
  var elements=plan.Request!.Circuit.Elements;
  foreach(var diode in elements.OfType<SpiceDiode>())
   Assert.Contains(elements.OfType<Resistor>(),r=>r.Id==diode.Id+"_gmin"&&r.Ohms==1e12&&r.P==diode.P&&r.N==diode.N);
 }
 [Fact]
 public void MosTransientUsesRemovedGminForStartupAndNativeDefaultTrapezoidal()
 {
  var plan=RequestedAnalysis.Prepare(DeckParser.Parse("MOS startup\nV1 d 0 1\nV2 g 0 1\nM1 d g 0 0 model\n.model model NMOS(LEVEL=1 CBD=1p)\n.tran 1n 100n\n.end\n",collectDeclarations:true));
  Assert.True(plan.Success,string.Join(',',plan.Diagnostics.Select(d=>d.Code)));
  var request=Assert.Single(Assert.Single(plan.Operations).Requests);
  var config=Assert.IsType<TransientConfigured>(request.Analysis).Options;
  Assert.Equal("trapezoidal",config.Method);
  Assert.Equal(1e-7,config.Adaptive!.Relative);Assert.Equal(1e-4,config.Adaptive.CurrentRelative);
  Assert.NotNull(config.InitialNewton!.GminContinuation);
  Assert.Null(config.InitialNewton.Continuation);
  Assert.Null(config.InitialNewton.GminSiemens);
 }
}
