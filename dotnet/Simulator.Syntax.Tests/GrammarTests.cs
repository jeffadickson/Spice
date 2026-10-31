using FsCheck;
using FsCheck.Xunit;
using Simulator.Syntax;
using Xunit;

namespace Simulator.Syntax.Tests;

public sealed class GrammarTests
{
    [Theory]
    [InlineData("R1 in 0 1k")]
    [InlineData("V1 in 0 DC 1 AC 2 90 SIN(0 1 1k)")]
    [InlineData("C1 in 0 {2*p+1n}")]
    [InlineData(".model modern NMOS (LEVEL=54 VTH0=0.4)")]
    [InlineData("M1 d g s b modern W=1u L=100n")]
    [InlineData("Q1 c b e bipolar\n.model bipolar NPN (IS=1e-14)")]
    [InlineData(".tran 1n 1u UIC\n.tran 2n 2u\n.ac dec 10 1 1meg")]
    [InlineData(".subckt stage a b\nR1 a b 1k\n.ends stage\nX1 in 0 stage")]
    [InlineData(".include \"this-file-must-not-be-loaded.cir\"")]
    [InlineData(".control\nshell touch forbidden\nquit\n.endc")]
    public void Accepts_structure_without_numerical_support(string body)
        => Assert.Equal("accepted_structure", DeckParser.Parse("Test\n"+body+"\n.end\n").Status);

    [Theory]
    [InlineData("R1 in 0")]
    [InlineData("M1 d g model")]
    [InlineData(".model onlyname")]
    [InlineData("R1 in 0 {1+}")]
    [InlineData("R1 in 0 {1+2")]
    [InlineData("R1 in 0 1k)")]
    [InlineData(".subckt stage a b\nR1 a b 1k")]
    [InlineData(".control\nprint v(1)")]
    [InlineData(".include \"unterminated")]
    public void Rejects_malformed_declared_productions(string body)
        => Assert.Equal("syntax_rejected", DeckParser.Parse("Test\n"+body+"\n.end\n").Status);

    [Fact]
    public void Unknown_extensions_are_gaps_not_success_or_proven_invalid()
        => Assert.Equal("grammar_gap", DeckParser.Parse("Test\n.vendor_extension foo\n.end\n").Status);

    [Fact]
    public void Continuations_retain_physical_source_spelling_and_span()
    {
        const string text="Title\r\nR1 in 0\r\n+ 1k\r\n.end\r\n";
        var result=DeckParser.Parse(text);
        Assert.Equal("accepted_structure",result.Status);
        var resistor=Assert.Single(result.Statements.Where(s=>s.Kind=="passive"));
        Assert.Equal(2,resistor.Range.Line);
        Assert.Equal("R1 in 0\r\n+ 1k",resistor.Text);
        Assert.Equal(resistor.Text,text.Substring(resistor.Range.Start,resistor.Range.Length));
    }

    [Fact]
    public void Bounds_nesting_without_recursing_on_hostile_input()
        => Assert.Equal("resource_rejected",DeckParser.Parse("Title\nR1 1 0 "+new string('{',129)+"1\n").Status);

    [Property(MaxTest=128, Replay="192,20261031")]
    public bool Positive_engineering_values_and_node_names_parse(PositiveInt resistance, NonNegativeInt node)
        => DeckParser.Parse($"Property\nR1 n{node.Get} 0 {resistance.Get}k\n.end\n").Status=="accepted_structure";
}
