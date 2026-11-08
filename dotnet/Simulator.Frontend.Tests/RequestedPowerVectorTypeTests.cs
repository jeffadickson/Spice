using Simulator.RequestedQualification;
using Simulator.Syntax;
using Xunit;

namespace Simulator.Frontend.Tests;

public sealed class RequestedPowerVectorTypeTests
{
    private const string Deck = """
        terminal reporting metadata
        V1 in 0 1
        R1 in 0 1k
        .control
        dc V1 0 2 1
        settype power q1:power
        display
        .endc
        .end
        """;

    [Fact]
    public void Terminal_power_type_keeps_original_analysis_and_records_reporting_omission()
    {
        var syntax=DeckParser.Parse(Deck,collectDeclarations:true);
        var plan=RequestedAnalysis.Prepare(syntax);
        Assert.True(plan.Success,string.Join(',',plan.Diagnostics.Select(d=>d.Code)));
        var operation=Assert.Single(plan.Operations);
        Assert.Equal("dc",operation.Kind);
        Assert.Contains(plan.ProgramOmissions,o=>o.Name=="settype"&&o.Reason=="vector_type_reporting_not_executed");
        var projection=OriginalProjection.Create(Deck,syntax,operation,plan);
        Assert.DoesNotContain("settype",projection.Text);
        Assert.Contains("dc V1 0 2 1",projection.Text);
    }

    [Theory]
    [InlineData("settype power q1:power\ndc V1 0 2 1")]
    [InlineData("dc V1 0 2 1\nsettype voltage q1:power")]
    [InlineData("dc V1 0 2 1\nsettype power time")]
    [InlineData("dc V1 0 2 1\nsettype power q1:power *")]
    [InlineData("dc V1 0 2 1\nsettype power q1:power; shell exit")]
    public void Metadata_outside_the_supported_terminal_shape_is_refused(string program)
    {
        var text=Deck.Replace("dc V1 0 2 1\nsettype power q1:power",program,StringComparison.Ordinal);
        var plan=RequestedAnalysis.Prepare(DeckParser.Parse(text,collectDeclarations:true));
        Assert.False(plan.Success);
    }
}
