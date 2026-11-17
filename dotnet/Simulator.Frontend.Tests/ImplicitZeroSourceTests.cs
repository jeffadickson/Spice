using System.Text.Json;
using Simulator.Client;
using Simulator.Syntax;
using Xunit;
namespace Simulator.Frontend.Tests;
/// <summary>SPICE's omitted independent-source value is a defined zero, distinct from an unresolved value.</summary>
public sealed class ImplicitZeroSourceTests
{
    /// <summary>Preserve lexical absence while producing the same complete physical request as explicit zero.</summary>
    [Theory][InlineData("V")][InlineData("I")]
    public void OmittedValueHasTheExplicitZeroPhysicalRequest(string family)
    {
        var omitted=Prepare(family,"");var authored=Prepare(family,"0");
        Assert.True(omitted.Success,string.Join(',',omitted.Diagnostics.Select(d=>d.Code)));
        Assert.True(authored.Success);
        Assert.Equal(JsonSerializer.Serialize(authored.Operations[0].Requests[0],WireFormat.Options),
            JsonSerializer.Serialize(omitted.Operations[0].Requests[0],WireFormat.Options));
        var syntax=DeckParser.Parse(Deck(family,""),collectDeclarations:true);
        var source=syntax.Declarations!.Primitives.Single(p=>p.Name==family+"probe");
        Assert.Null(source.Value);Assert.NotNull(source.Excitation);Assert.Null(source.Excitation!.Dc);
    }
    /// <summary>Missing passive values, unresolved expressions and unsupported source tails remain refusals.</summary>
    [Theory][InlineData("R","")][InlineData("C","")][InlineData("L","")]
    [InlineData("V","missing")][InlineData("I","missing")][InlineData("V","DC")]
    [InlineData("V","0 garbage")]
    public void UnsupportedOrUnresolvedValuesNeverBecomeZero(string family,string tail)
    {
        var plan=Prepare(family,tail);Assert.False(plan.Success);Assert.Empty(plan.Operations);
    }
    /// <summary>The default applies at an instantiated sheet/subcircuit boundary as well as the root.</summary>
    [Fact]
    public void HierarchicalZeroVoltageSensorPrepares()
    {
        var plan=RequestedAnalysis.Prepare(DeckParser.Parse("Nested sensor\nVdrive in 0 1\nR1 in out 1k\nX1 out 0 sense\n.subckt sense p n\nVsense p n\n.ends sense\n.op\n.end\n",collectDeclarations:true));
        Assert.True(plan.Success,string.Join(',',plan.Diagnostics.Select(d=>d.Code)));
        Assert.Single(plan.Operations[0].Requests[0].Circuit.Elements.OfType<Voltage>().Where(v=>v.Volts==0));
    }
    /// <summary>Unknown authored identifiers cannot enter the missing-clause default path.</summary>
    [FsCheck.Xunit.Property(MaxTest=64,Replay="2162,20261117")]
    public bool AuthoredUnknownIdentifiersAreNotDefaulted(int suffix)
    {
        var plan=Prepare("V","missing_"+unchecked((uint)suffix).ToString(System.Globalization.CultureInfo.InvariantCulture));
        return !plan.Success&&plan.Operations.IsEmpty;
    }
    private static string Deck(string family,string tail)=>"Implicit source\nVdrive in 0 1\nR1 in out 1k\n"+family+"probe out 0 "+tail+"\n.op\n.end\n";
    private static RequestedAnalysisPlan Prepare(string family,string tail)=>RequestedAnalysis.Prepare(DeckParser.Parse(Deck(family,tail),collectDeclarations:true));
}
