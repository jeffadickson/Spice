using Simulator.Syntax;
using Xunit;

namespace Simulator.Syntax.Tests;

public sealed class DeviceFamilyTests
{
    [Theory]
    [InlineData("N1 out custom_osdi")]
    [InlineData("N1 d g s b thermal model L=1u")]
    [InlineData("P1 1 2 0 3 4 0 line_model")]
    [InlineData("Y1 1 0 2 0 txl_model L=1m")]
    [InlineData("U1 in out 0 urc_model L=20m")]
    [InlineData(".model numerical NUMD level=1\n+ *** numerical model comment\n+ options defa=1p\n+ x.mesh loc=0 n=10")]
    [InlineData(".sp dec 10 1 10G")]
    [InlineData(".sparam dec 10 1 10G")]
    [InlineData(".model foo code_model (pair=<1 -2>)")]
    public void Accepts_named_structural_families(string body)
        => Assert.Equal("accepted_structure",DeckParser.Parse("Title\n"+body+"\n.end\n").Status);

    [Theory]
    [InlineData("N1 out")]
    [InlineData("P1 1 0 model")]
    [InlineData("Y1 1 0 model")]
    [InlineData("U1 1 0 model")]
    [InlineData(".model foo code_model (pair=<1>)")]
    public void Rejects_missing_terminals_models_and_complex_components(string body)
        => Assert.Equal("syntax_rejected",DeckParser.Parse("Title\n"+body+"\n.end\n").Status);
}
