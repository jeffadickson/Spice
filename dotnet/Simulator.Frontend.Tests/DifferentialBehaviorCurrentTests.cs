using Simulator.Client;
using Simulator.Syntax;
using Xunit;
namespace Simulator.Frontend.Tests;

/// <summary>Keep differential behavioral currents out of cancelling common-mode KCL injections.</summary>
public sealed class DifferentialBehaviorCurrentTests
{
    /// <summary>The typed bridge retains a differential control instead of injecting two large currents.</summary>
    [Fact]
    public void MultiTermCurrentUsesOneUnitTransconductanceAfterVoltageEvaluation()
    {
        var deck=DeckParser.Parse("Differential current\nVrail rail 0 16\nVin in 0 15.999999999068677425384521484375\nB1 out 0 I={1073741824*(v(rail)-v(in))}\nRload out 0 1\n.op\n.end\n",collectDeclarations:true);
        var plan=RequestedAnalysis.Prepare(deck);
        Assert.True(plan.Success,string.Join(',',plan.Diagnostics.Select(d=>d.Code)));
        var circuit=Assert.Single(Assert.Single(plan.Operations).Requests).Circuit;
        var current=Assert.Single(circuit.Elements.OfType<Vccs>());
        Assert.Equal(1,current.Siemens);
        Assert.Contains(circuit.Elements,e=>e is Vcvs);
    }
}
