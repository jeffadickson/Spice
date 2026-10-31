using FsCheck;
using FsCheck.Xunit;
using Simulator.Syntax;
using Xunit;

namespace Simulator.Syntax.Tests;

public sealed class PredictionTests
{
    [Theory]
    [InlineData("R1 1 0 1k")]
    [InlineData("R1 1 0 {a+}")]
    [InlineData("N1 d g s b model L=1u")]
    [InlineData("a1 [%vd(1 0)] [out] model")]
    [InlineData(".subckt 283 a b\nR1 a b 1k\n.ends 283")]
    public void Sll_with_fallback_matches_full_ll_for_reference_productions(string body)
    {
        var text="Title\n"+body+"\n.end\n";
        var fast=DeckParser.Parse(text);
        var reference=DeckParser.Parse(text,new ParserLimits(useSllFirst:false));
        Assert.Equal(reference.Status,fast.Status);
        Assert.True(reference.Statements.SequenceEqual(fast.Statements),"Statement values and physical ranges must match");
    }

    [Property(MaxTest=256,Replay="492,20261031")]
    public bool Prediction_strategies_agree_on_bounded_malformed_text(NonNull<string> value)
    {
        var text="Fuzz\n"+value.Get[..Math.Min(value.Get.Length,128)]+"\n";
        var fast=DeckParser.Parse(text);
        var reference=DeckParser.Parse(text,new ParserLimits(useSllFirst:false));
        return fast.Status==reference.Status && fast.Statements.SequenceEqual(reference.Statements);
    }
}
