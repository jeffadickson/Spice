using Simulator.Syntax;
using Xunit;
namespace Simulator.Frontend.Tests;
public sealed class ReadinessTests
{
    [Fact]
    public void Audit_retains_multiple_structural_layers_instead_of_only_the_first_failure()
    {
        var deck="Audit\n.options reltol=1m\nV1 a 0 2\nQ1 a a 0 transistor\nD1 a 0 diode\nF1 b 0 V1 2\n.model transistor npn (bf=100 VAF=100)\n.model diode d (is=1p)\n.end\n";
        var audit=CircuitReadiness.Audit(DeckParser.Parse(deck,collectDeclarations:true));
        Assert.False(audit.CanPrepare);Assert.True(audit.ActiveGraphComplete);Assert.False(audit.NumericalValidationExhaustive);
        Assert.Contains(audit.Issues,i=>i.Layer=="directive_policy");
        Assert.DoesNotContain(audit.Issues,i=>i.Layer=="runtime_model");
        Assert.DoesNotContain(audit.Issues,i=>i.Kind=="diode");
        Assert.DoesNotContain(audit.Issues,i=>i.Layer=="parser_to_runtime"&&i.Kind=="icontrol");
        Assert.Contains(audit.Issues,i=>i.Layer=="model_contract"&&i.Code=="unsupported_bjt_profile");
        Assert.Contains(audit.Models,m=>m.ModelFamily=="d"&&m.ParameterAssignments.Contains("is"));
        Assert.Single(audit.PreparationDiagnostics);
    }
    [Fact]
    public void Unused_device_bodies_are_not_claimed_as_active_blockers()
    {
        var syntax=DeckParser.Parse("Unused\nV1 a 0 2\nR1 a 0 1k\n.subckt spare p n\nM1 p p n n unknown\n.ends\n.end\n",collectDeclarations:true);
        var audit=CircuitReadiness.Audit(syntax);Assert.True(audit.CanPrepare);Assert.DoesNotContain(audit.Issues,i=>i.Kind=="mos");
    }
    [Fact]
    public void Unbound_hierarchy_is_explicitly_incomplete()
    {
        var audit=CircuitReadiness.Audit(DeckParser.Parse("Missing\nX1 a 0 absent\n.end\n",collectDeclarations:true));
        Assert.False(audit.ActiveGraphComplete);Assert.Contains(audit.Issues,i=>i.Layer=="binding");
    }
}
