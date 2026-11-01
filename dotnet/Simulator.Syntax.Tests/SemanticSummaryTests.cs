using Xunit;
namespace Simulator.Syntax.Tests;
public sealed class SemanticSummaryTests
{
    [Fact]
    public void Stages_have_separate_denominators_and_no_reference_does_not_claim_binding()
    {
        var catalog=DeckParser.Parse("Title\n.param gain=2\nR1 a 0 1k\n.end\n",collectDeclarations:true).Declarations!;
        var summary=SemanticSummary.From(catalog);
        Assert.Equal("no_references_observed",summary.ReferenceStatus);Assert.Equal(0,summary.References);
        Assert.Equal("evaluated_root_scope",summary.ParameterStatus);Assert.Equal(1,summary.ParameterValues);
    }
    [Fact]
    public void Bound_missing_and_unsupported_contracts_are_counted_independently()
    {
        var catalog=DeckParser.Parse("Title\nX1 a b cell\nD1 a b absent\nD2 a b wrong\n.model wrong npn\n.subckt cell p n\n.ends\n.end\n",collectDeclarations:true).Declarations!;
        var summary=SemanticSummary.From(catalog);
        Assert.Equal(3,summary.References);Assert.Equal(1,summary.ReferenceCounts["Bound"]);Assert.Equal(1,summary.ReferenceCounts["Missing"]);Assert.Equal(1,summary.ReferenceCounts["Unsupported"]);
        Assert.Equal(3,summary.ReferenceCounts.Values.Sum());Assert.Equal(2,summary.Ports);
    }
    [Fact]
    public void External_resources_remain_unopened_in_the_observer_profile()
    {
        var catalog=DeckParser.Parse("Title\n.include 'definitely-not-opened.inc'\nX1 a b missing\n.end\n",collectDeclarations:true).Declarations!;
        var summary=SemanticSummary.From(catalog);
        Assert.Equal("deferred_parameters",summary.ParameterStatus);Assert.Equal(1,summary.ReferenceCounts["Deferred"]);Assert.Equal(0,summary.ParameterValues);
        Assert.Throws<ArgumentNullException>(()=>SemanticSummary.From(null!));
    }
}
