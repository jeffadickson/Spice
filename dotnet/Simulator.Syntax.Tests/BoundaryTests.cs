using FsCheck;
using FsCheck.Xunit;
using Simulator.Syntax;
using Xunit;

namespace Simulator.Syntax.Tests;

public sealed class BoundaryTests
{
    [Theory]
    [InlineData(".print ac v(out)")]
    [InlineData(".plot tran v(out)")]
    [InlineData(".probe v(out)")]
    [InlineData(".save v(out)")]
    [InlineData(".meas tran vmax max v(out)")]
    [InlineData(".measure tran vmax max v(out)")]
    [InlineData(".options reltol=1e-3")]
    [InlineData(".option abstol=1p")]
    [InlineData(".temp 27")]
    [InlineData(".global vdd")]
    [InlineData(".lib TT\n.endl TT")]
    [InlineData(".func f(x) {x+1}")]
    [InlineData(".ic v(out)=1")]
    [InlineData(".nodeset v(out)=0")]
    [InlineData(".if (1)\n.elseif (0)\n.else\n.endif")]
    [InlineData(".csparam supply=5")]
    [InlineData(".title alternate")]
    [InlineData(".backanno")]
    [InlineData(".four 1k v(out)")]
    [InlineData(".tf v(out) V1")]
    [InlineData(".noise v(out) V1 dec 10 1 1meg")]
    [InlineData(".pz in 0 out 0 vol pz")]
    [InlineData(".sens v(out)")]
    [InlineData(".disto dec 10 1 1meg")]
    [InlineData(".width out=80")]
    [InlineData(".savebias \"bias.dat\"")]
    [InlineData(".loadbias \"bias.dat\"")]
    public void Recognizes_named_directive_structure(string body)
        => Assert.Equal("accepted_structure",DeckParser.Parse("Title\n"+body+"\n.end\n").Status);

    [Fact]
    public void Unspecified_device_family_is_a_grammar_gap()
        => Assert.Equal("grammar_gap",DeckParser.Parse("Title\n@custom1 [in] [out] digital_model\n.end\n").Status);

    [Fact]
    public void Unquoted_include_paths_are_inert_syntax()
        => Assert.Equal("accepted_structure",DeckParser.Parse("Title\n.include ../not-loaded/model.lib\n.end\n").Status);

    [Fact]
    public void Boundary_nesting_guard_cannot_be_bypassed_by_leading_closers()
    {
        var result=DeckParser.Parse("Title\n"+new string(')',130)+new string('(',129)+"1\n");
        Assert.Equal("resource_rejected",result.Status);
    }

    [Fact]
    public void Exact_nesting_limit_and_repeated_shallow_groups_are_accepted()
    {
        Assert.Equal("accepted_structure",DeckParser.Parse("Title\nR1 1 0 "+new string('(',128)+"1"+new string(')',128)+"\n").Status);
        Assert.Equal("accepted_structure",DeckParser.Parse("Title\nR1 1 0 "+string.Join(' ',Enumerable.Repeat("(1)",129))+"\n").Status);
    }

    [Fact]
    public void Title_column_and_missing_final_newline_are_preserved()
    {
        var result=DeckParser.Parse("Exact title\r   R1 1 0 1k");
        Assert.Equal("accepted_structure",result.Status);
        Assert.Equal("Exact title",result.Title);
        var card=Assert.Single(result.Statements);
        Assert.Equal(4,card.Range.Column);
        Assert.Equal(2,card.Range.Line);
        Assert.Equal("R1 1 0 1k",card.Text);
    }

    [Fact]
    public void Input_and_token_bounds_report_reasons()
    {
        Assert.Equal("accepted_structure",DeckParser.Parse(new string('t',1_048_576)).Status);
        var oversized=DeckParser.Parse(new string('t',1_048_577));
        Assert.Equal("resource_rejected",oversized.Status);
        Assert.Contains("1,048,576",Assert.Single(oversized.Diagnostics).Message);
        Assert.Equal("accepted_structure",DeckParser.Parse("Title\n"+new string('\n',100_000)).Status);
        var many=DeckParser.Parse("Title\n"+string.Concat(Enumerable.Repeat("R1 1 0 1\n",15_000)));
        Assert.Equal("resource_rejected",many.Status);
        Assert.Contains("100,000",Assert.Single(many.Diagnostics).Message);
    }

    [Fact]
    public void Diagnostic_counts_lengths_and_locations_are_bounded()
    {
        var gaps=DeckParser.Parse("Title\n"+string.Concat(Enumerable.Repeat(".unknown 1\n",40)));
        Assert.Equal("grammar_gap",gaps.Status);
        Assert.Equal(32,gaps.Diagnostics.Length);
        Assert.All(gaps.Diagnostics,d=>{Assert.Equal("grammar_gap",d.Code);Assert.Contains(".unknown",d.Message);Assert.Equal(1,d.Column);});
        var malformed=DeckParser.Parse("Title\nR1 1 0 "+new string('?',1000)+"\n");
        Assert.Equal("syntax_rejected",malformed.Status);
        Assert.NotEmpty(malformed.Diagnostics);
        Assert.All(malformed.Diagnostics,d=>{Assert.Equal("syntax",d.Code);Assert.InRange(d.Message.Length,1,512);Assert.True(d.Column>0);});
    }

    [Fact]
    public void Null_is_an_explicit_caller_error() => Assert.Throws<ArgumentNullException>(()=>DeckParser.Parse(null!));

    [Fact]
    public void Generated_rule_guard_bounds_unary_and_hierarchy_recursion()
    {
        var unary=DeckParser.Parse("Title\nB1 1 0 v={"+new string('!',4096)+"1}\n.end\n");
        Assert.Equal("resource_rejected",unary.Status);
        Assert.Contains("recursion",Assert.Single(unary.Diagnostics).Message);
        var nested=DeckParser.Parse("Title\n"+string.Concat(Enumerable.Repeat(".subckt s a b\n",600))
            +string.Concat(Enumerable.Repeat(".ends\n",600))+".end\n");
        Assert.Equal("resource_rejected",nested.Status);
        // Each individual group/operator chain is under its token cap; only
        // the generated-rule guard bounds their combined recursive structure.
        var mixed=DeckParser.Parse("Title\nB1 1 0 v={"+
            string.Concat(Enumerable.Repeat(new string('!',100)+"(",8))+"1"+new string(')',8)+"}\n");
        Assert.Equal("resource_rejected",mixed.Status);
        Assert.Contains("rule depth",Assert.Single(mixed.Diagnostics).Message);
    }

    [Property(MaxTest=512,Replay="362,20261031")]
    public bool Malformed_text_never_escapes_the_syntax_result(NonNull<string> value)
    {
        var body=value.Get.Length>1024?value.Get[..1024]:value.Get;
        var text="Fuzz\n"+body+"\n";
        var result=DeckParser.Parse(text);
        return new[]{"accepted_structure","syntax_rejected","grammar_gap","resource_rejected"}.Contains(result.Status)
            && result.Diagnostics.Length<=32
            && result.Statements.All(s=>s.Range.Start>=0 && s.Range.Start+s.Range.Length<=text.Length);
    }
}

