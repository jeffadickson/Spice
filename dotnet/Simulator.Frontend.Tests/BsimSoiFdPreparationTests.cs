using Simulator.Syntax;
using Xunit;

namespace Simulator.Frontend.Tests;

public sealed class BsimSoiFdPreparationTests
{
    [Theory]
    [InlineData("gmin=1e-25 itl1=500 noacct", true)]
    [InlineData("itl1=500 gmin=1E-25", true)]
    [InlineData("gmin=1e-24 itl1=500", false)]
    [InlineData("gmin=1e-25 itl1=501", false)]
    [InlineData("gmin=1e-25 gmin=1e-25 itl1=500", false)]
    [InlineData("gmin=1e-25 itl1=500 abstol=1e-9", false)]
    public void Only_exact_supported_settings_are_recognized(string settings, bool recognized)
    {
        var syntax = DeckParser.Parse($"Options\nV1 d 0 1\n.option {settings}\n.op\n.end\n",
            collectDeclarations:true);
        var options = Assert.Single(syntax.Declarations!.Cards.Where(c => c.Name == ".option"));
        Assert.Equal(recognized, options.BsimSoiFdStaticOptions);
        var plan = RequestedAnalysis.Prepare(syntax);
        Assert.Empty(plan.Operations);
        Assert.Contains(plan.Diagnostics, d => d.Code == (recognized
            ? "unsupported_bsim_soi_fd_option_context" : "unsupported_physical_directive"));
    }

    [Fact]
    public void Different_level_55_card_cannot_select_the_pinned_provider()
    {
        const string deck = "FD\nVd d 0 1\nVg g 0 1\nM1 d g 0 0 n1 w=10u l=.25u\n"
            + ".model n1 nmos level=55 tox=4.5n\n.option gmin=1e-25 itl1=500\n.op\n.end\n";
        var plan = RequestedAnalysis.Prepare(DeckParser.Parse(deck, collectDeclarations:true));
        Assert.Empty(plan.Operations);
        Assert.Contains(plan.Diagnostics, d => d.Code == "unsupported_bsim_soi_fd_profile");
    }
}
