using Simulator.Syntax;
using Xunit;

namespace Simulator.Syntax.Tests;

public sealed class CommonSyntaxTests
{
    [Theory]
    [InlineData("R1 Net-_Q1-Pad1_ +5V 1k")]
    [InlineData("R1 /power/net -VDD 1k")]
    [InlineData("R1 signal:1 0 1k")]
    [InlineData("R1 3_net 0 1k")]
    [InlineData("R1 bus[3] 1:2 1k")]
    [InlineData("A1 %v[1] out[2] model")]
    [InlineData("B1 1 0 V={v(3_net)+v(bus[2])}")]
    [InlineData("R1 vü 0 1k")]
    [InlineData("B1 1 0 V={a-b+2*c}")]
    [InlineData(".lib D:\\PDK\\models\\corner.lib TT")]
    [InlineData("R1 in 0\n* continuation comment\n+ 1k")]
    [InlineData(".model numeric NUMD level=1\n+ models srh ^aval")]
    [InlineData("$ standalone comment\nR1 1 0 1k $ inline comment")]
    [InlineData(".model n NUMD\n$ comment before continuation\n+ x.mesh loc=0 n=10")]
    [InlineData(".subckt 283 1A 1B\nR1 1A 1B 1k\n.ends 283")]
    [InlineData(".model 1 D (IS=1p)\nD1 1 0 1")]
    [InlineData("R1 (1 0) 1k\nV1 (2,0) 1")]
    [InlineData(".param and=\"0001\" or=\"0111\" both=or{and}")]
    [InlineData("U1 NAND(2) $G_DPWR $G_DGND D0A D0B Q0BAR DLY_XYZ IO_STD MNTYMXDLY=0")]
    public void Recognizes_reference_lexical_forms(string body)
        => Assert.Equal("accepted_structure",DeckParser.Parse("Title\n"+body+"\n.end\n").Status);

    [Theory]
    [InlineData("R1 in 0 {a+}")]
    [InlineData("R1 in 0 {a-}")]
    [InlineData("R1 in 0 {1**}")]
    public void Node_spelling_does_not_hide_malformed_braced_expressions(string body)
        => Assert.Equal("syntax_rejected",DeckParser.Parse("Title\n"+body+"\n.end\n").Status);

    [Fact]
    public void Expanded_profile_admits_larger_valid_input_without_changing_default()
    {
        var text=new string('t',1_048_577);
        Assert.Equal("resource_rejected",DeckParser.Parse(text).Status);
        Assert.Equal("accepted_structure",DeckParser.Parse(text,new ParserLimits(2_097_152,200_000)).Status);
    }

    [Fact]
    public void Explicit_ng_script_header_selects_inert_command_file_structure()
    {
        const string input="*ng_script\n.control\nbegin\nshell must_not_run\nend\n";
        var result=DeckParser.Parse(input);
        Assert.Equal("accepted_structure",result.Status);
        Assert.Equal("command_script",result.Format);
        Assert.Contains(result.Statements,s=>s.Kind=="command" && s.Text.Contains("must_not_run"));
    }

    [Theory]
    [InlineData(0,100)]
    [InlineData(100,0)]
    [InlineData(67_108_865,100)]
    [InlineData(100,4_000_001)]
    public void Rejects_unbounded_or_empty_profiles(int characters,int tokens)
        => Assert.Throws<ArgumentOutOfRangeException>(()=>new ParserLimits(characters,tokens));

    [Theory]
    [InlineData(1,1)]
    [InlineData(67_108_864,4_000_000)]
    public void Admits_exact_profile_endpoints_without_allocating_input(int characters,int tokens)
    {
        var limits=new ParserLimits(characters,tokens);
        Assert.Equal(characters,limits.MaxInputCharacters);
        Assert.Equal(tokens,limits.MaxTokens);
    }
}
