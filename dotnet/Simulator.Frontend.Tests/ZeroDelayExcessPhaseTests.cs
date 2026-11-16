using System.Text.Json;
using Simulator.Client;
using Simulator.Syntax;
using Xunit;
namespace Simulator.Frontend.Tests;
public sealed class ZeroDelayExcessPhaseTests
{
    [Theory]
    [InlineData("0")]
    [InlineData("1")]
    public void Zero_delay_algorithm_choice_preserves_the_complete_physical_request(string flag)
    {
        var original=Prepare("");var changed=Prepare($".options device newexcessphase={flag}");
        Assert.True(original.Success);Assert.True(changed.Success,string.Join(',',changed.Diagnostics.Select(d=>d.Code)));
        Assert.Equal(JsonSerializer.Serialize(original.Operations[0].Requests[0],WireFormat.Options),JsonSerializer.Serialize(changed.Operations[0].Requests[0],WireFormat.Options));
        Assert.Contains(changed.Observations,o=>o.Reason=="zero_delay_excess_phase_equivalence");
    }
    [Theory]
    [InlineData(".options device newexcessphase=2", "")]
    [InlineData(".options device newexcessphase={1}", "")]
    [InlineData(".options device newexcessphase=1 voltlim=0", "")]
    [InlineData(".options device newexcessphase=1\n.options device newexcessphase=0", "")]
    [InlineData(".options device newexcessphase=1", " PTF=90")]
    [InlineData(".options device newexcessphase=1", " PTF=0")]
    [InlineData(".subckt unused x y\n.options device newexcessphase=1\n.ends", "")]
    public void Unknown_companions_duplicates_and_real_delay_remain_atomic_refusals(string card,string parameters)
    {
        var plan=Prepare(card,parameters);Assert.False(plan.Success);Assert.Empty(plan.Operations);
    }
    [FsCheck.Xunit.Property(MaxTest=64,Replay="2161,20261117")]
    public bool Other_integer_selectors_never_acquire_zero_delay_admission(int input)
    {
        var flag=2+(uint)input%10000;
        var plan=Prepare(".options device newexcessphase="+flag.ToString(System.Globalization.CultureInfo.InvariantCulture));
        return !plan.Success&&plan.Operations.IsEmpty;
    }
    private static Simulator.Frontend.RequestedAnalysisPlan Prepare(string card,string parameters="")=>Simulator.Frontend.RequestedAnalysis.Prepare(DeckParser.Parse(
        $"zero delay\nVCC supply 0 3\nVB b 0 .65 AC 1m\nR1 supply c 1k\nQ1 c b 0 mod\n.model mod NPN(IS=1f BF=100 CJC=1p TF=1n{parameters})\n{card}\n.ac lin 1 1k 1k\n.end\n",collectDeclarations:true));
}
