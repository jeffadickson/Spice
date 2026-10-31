using Simulator.Syntax;
using Xunit;

namespace Simulator.Syntax.Tests;

public sealed class ExtendedFamilyTests
{
    [Fact]
    public void Digital_card_mode_does_not_leak_into_closed_quoted_expressions()
    {
        foreach(var state in "01xzrf")
        {
            var text="Title\nU1 LOGICEXP(1,1) DPWR DGND A OUT IO_STD LOGIC: OUT={A=='1}\n"
                +$"B1 1 0 V={{'{state} + 2'}}\n.func f() '{state} + 1'\n.end\n";
            Assert.Equal("accepted_structure",DeckParser.Parse(text).Status);
        }
    }

    [Theory]
    [InlineData("U1 LOGICEXP(2,1) DPWR DGND A B OUT IO_STD LOGIC: OUT={A=='1 & B!='0}")]
    [InlineData(".sndprint v(out)")]
    [InlineData("B1 1 0 V={'1 + 2'}")]
    [InlineData(".func f() 'x + 1'")]
    [InlineData("U1 LOGICEXP\n+ (2,1) DPWR DGND A B OUT IO_STD LOGIC: OUT={A=='1 & B!='0}")]
    [InlineData("U1\n* header comment\n+ LOGICEXP(2,\n+ 1) DPWR DGND A B OUT IO_STD LOGIC: OUT={A=='1 & B!='0}")]
    [InlineData("U1 in out 0 urc_model L={'1 + 2'}")]
    [InlineData("M1 d g s power\n.model power VDMOS")]
    [InlineData("R? 1 0 1k")]
    [InlineData("a1 (pint1 pint2) (mc1 0) primary")]
    [InlineData("a1 %vd([out1 0]) filesrc")]
    [InlineData("U1 LOGICEXP(2,1) $G_DPWR $G_DGND A B OUT D0_GATE IO_STD LOGIC: OUT={A & B}")]
    [InlineData("U1 NAND(2)\n+ $G_DPWR $G_DGND A B OUT D0_GATE IO_STD MNTYMXDLY=0")]
    [InlineData("U1 PINDLY(1,1,0) DPWR DGND A EN OUT IO_STD TRISTATE: ENABLE LO=EN OUT={CASE(TRN_Z$,DELAY(-1,15n,28n))}")]
    [InlineData("B1 1 0 V=~(~v(9)&v(8)) + 0.5")]
    [InlineData("B1 1 0 I=Isat*(exp(v(1)/VT)-1)")]
    [InlineData("B1 1 0 I=0.05*(1+tanh((v(g)-0.3)/0.15))")]
    [InlineData("B1 1 0 I=ternary_fcn(TIME<'deltat',0,2.5m*(exp(-(TIME-'deltat')/'alpha'))) ")]
    [InlineData(".func border(x) if(ddt(x)>0,1,0)")]
    [InlineData(".if (mostype==1)\nR1 1 0 1k\n.endif")]
    [InlineData(".subckt count 10\nR1 10 0 1k\n.ends count 10")]
    [InlineData(".model foo D (IS=1p\n+ N=1")]
    [InlineData(".model foo D IS=1p)")]
    [InlineData("# hash comment\n.sndparam rate=44100\n.opt noacct")]
    public void Accepts_remaining_documented_structural_families(string body)
    {
        var parsed=DeckParser.Parse("Title\n"+body+"\n.end\n");
        Assert.Equal("accepted_structure",parsed.Status);
        if(body.Contains("$G_DPWR"))Assert.Contains(parsed.Statements,s=>s.Text.Contains("$G_DPWR"));
    }

    [Theory]
    [InlineData("B1 1 0 V=1+")]
    [InlineData(".func f(x) x+")]
    [InlineData("B1 1 0 V=(1+2")]
    public void Bare_expression_mode_does_not_accept_missing_operands_or_closers(string body)
        => Assert.Equal("syntax_rejected",DeckParser.Parse("Title\n"+body+"\n.end\n").Status);
}




