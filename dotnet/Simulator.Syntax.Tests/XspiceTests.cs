using FsCheck;
using FsCheck.Xunit;
using Simulator.Syntax;
using Xunit;

namespace Simulator.Syntax.Tests;

public sealed class XspiceTests
{
    [Theory]
    [InlineData("A1 1 2 amp")]
    [InlineData("a1 [~1 2] 3 nand1")]
    [InlineData("a2 %i[1 2] 3 summer1")]
    [InlineData("a21 %i[1 %vd(2 5) 7 10] 3 sum2")]
    [InlineData("a1 %vd(in 0) %id(out 0) transfer")]
    [InlineData("a1 [%vd in 0] [out] comparator")]
    [InlineData("a1 %vnam(V1) out gain_model")]
    [InlineData("a1 null [~in out] digital")]
    [InlineData("a1 %my_udn(input) output custom")]
    public void Recognizes_code_model_ports_without_model_loading(string card)
    {
        var parsed=DeckParser.Parse("Title\n"+card+"\n.end\n");
        Assert.Equal("accepted_structure",parsed.Status);
        Assert.Contains(parsed.Statements,s=>s.Kind=="xspice");
    }

    [Theory]
    [InlineData("a1 [1 2 out model")]
    [InlineData("a1 %vd(1) out model")]
    [InlineData("a1 %vd(1 2 3) out model")]
    [InlineData("a1 % out model")]
    [InlineData("a1 [] out model")]
    [InlineData("a1 ~ model")]
    public void Rejects_malformed_port_structure(string card)
        => Assert.Equal("syntax_rejected",DeckParser.Parse("Title\n"+card+"\n.end\n").Status);

    [Property(MaxTest=128,Replay="481,20261031")]
    public bool Generated_differential_vectors_keep_polarity_spelling(NonNegativeInt positive,NonNegativeInt negative)
    {
        var card=$"A1 [%vd({positive.Get} {negative.Get})] [~out] model";
        var parsed=DeckParser.Parse("Title\n"+card+"\n");
        return parsed.Status=="accepted_structure" && parsed.Statements.Single().Text==card;
    }
}
