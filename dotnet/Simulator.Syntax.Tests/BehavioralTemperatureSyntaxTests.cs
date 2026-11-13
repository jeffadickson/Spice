using Simulator.Syntax;
using Xunit;
namespace Simulator.Syntax.Tests;

/// <summary>Physical admission does not erase generated header and attribute provenance.</summary>
public sealed class BehavioralTemperatureSyntaxTests
{
    [Fact]
    public void Additional_attributes_keep_ranges_and_do_not_change_strict_support_flag()
    {
        const string text="Metadata\nV1 input 0 1\nB1 out 0 V=v(input) TC1=.001 TC2=1u\n.temp -73\n.op\n.end\n";
        var syntax=DeckParser.Parse(text,collectDeclarations:true);var catalog=syntax.Declarations!;
        var source=Assert.Single(catalog.BehavioralSources);
        Assert.False(source.Supported);Assert.True(source.HeaderSupported);
        Assert.Equal(2,source.Parameters.Length);
        Assert.All(source.Parameters,p=>{
            Assert.Equal(3,p.Range.Line);
            Assert.Equal(p.Value.Text,text.Substring(p.Value.Range.Start,p.Value.Range.Length));
        });
        var temperature=Assert.Single(catalog.Cards.Where(c=>c.Name==".temp")).OperatingTemperature!;
        Assert.Equal("-73",temperature.Text);Assert.Equal(4,temperature.Range.Line);
    }

    [Fact]
    public void Opaque_tail_is_not_misrepresented_as_a_complete_typed_header()
    {
        var syntax=DeckParser.Parse("Opaque\nB1 out 0 V=v(input) unknown\n.end\n",collectDeclarations:true);
        var source=Assert.Single(syntax.Declarations!.BehavioralSources);
        Assert.False(source.HeaderSupported);Assert.False(source.Supported);
    }
}
