using Simulator.Client;
using Simulator.Syntax;
using Xunit;

namespace Simulator.Frontend.Tests;

public sealed class RequestedDcNewtonTests
{
    [Fact]
    public void Requested_nonlinear_dc_uses_a_bounded_independent_newton_budget()
    {
        var deck = DeckParser.Parse("BJT sweep\nVb b 0 .7\nVc c 0 1\nQ1 c b 0 qm\n.model qm NPN\n.op\n.dc Vb .8 .9 .1\n.end\n",
            collectDeclarations: true);
        var plan = RequestedAnalysis.Prepare(deck);
        Assert.True(plan.Success, string.Join(',', plan.Diagnostics.Select(d => d.Code)));
        Assert.IsType<OperatingPoint>(Assert.Single(plan.Operations[0].Requests).Analysis);
        Assert.All(plan.Operations[1].Requests, point => Assert.Equal(
            new NewtonOptions(MaxIterations: 200, Continuation: new(InitialStep: 1)),
            Assert.IsType<OperatingPointNewton>(point.Analysis).Options));
    }
}
