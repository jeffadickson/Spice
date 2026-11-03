using System.Text.Json;
using Simulator.Client;
using Simulator.Syntax;
using Xunit;
namespace Simulator.Frontend.Tests;

public sealed class InitialNewtonPolicyTests
{
    [Fact]
    public void Requested_nonlinear_transient_declares_bounded_source_bias_policy()
    {
        var syntax=DeckParser.Parse("Bias policy\nV1 s 0 1\nR1 s out 1k\nD1 out 0 model\n.model model D(IS=1p)\n.tran 1u 2u\n.end\n",collectDeclarations:true);
        var plan=RequestedAnalysis.Prepare(syntax);Assert.True(plan.Success);
        var request=Assert.Single(Assert.Single(plan.Operations).Requests);
        using var encoded=JsonDocument.Parse(JsonSerializer.Serialize(request,WireFormat.Options));
        var policy=encoded.RootElement.GetProperty("analysis").GetProperty("options").GetProperty("initial_newton");
        Assert.Equal(50,policy.GetProperty("max_iterations").GetInt32());
        Assert.Equal(64,policy.GetProperty("continuation").GetProperty("max_stages").GetInt32());
        Assert.False(policy.TryGetProperty("gmin_siemens",out _));
        Assert.False(policy.TryGetProperty("gmin_continuation",out _));
    }
    [Fact]
    public void Linear_transient_wire_shape_keeps_initial_policy_absent()
    {
        var syntax=DeckParser.Parse("Linear policy\nV1 out 0 1\nR1 out 0 1k\n.tran 1u 2u\n.end\n",collectDeclarations:true);
        var plan=RequestedAnalysis.Prepare(syntax);Assert.True(plan.Success);
        var request=Assert.Single(Assert.Single(plan.Operations).Requests);
        using var encoded=JsonDocument.Parse(JsonSerializer.Serialize(request,WireFormat.Options));
        Assert.False(encoded.RootElement.GetProperty("analysis").GetProperty("options").TryGetProperty("initial_newton",out _));
    }
}
