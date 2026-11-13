using Simulator.Client;
using Simulator.Syntax;
using Xunit;
namespace Simulator.Frontend.Tests;

/// <summary>Intrinsic-node and polarity contracts for the bounded VAF profile.</summary>
public sealed class BjtEarlyChargeTests
{
    [Theory][InlineData("NPN",1)][InlineData("PNP",-1)]
    public void Early_transport_and_charge_share_intrinsic_collector_control(string family,int sign)
    {
        var deck=$"Early charge\nVb b 0 {sign*.6}\nVc c 0 {sign*3}\nQ1 c b 0 qm\n.model qm {family}(IS=1f BF=50 VA=50 TF=2n TR=1n CJE=2p CJC=3p RB=10 RC=20 RE=5)\n.end\n";
        var prepared=OperatingPointSelection.Select(DeckParser.Parse(deck,collectDeclarations:true)).Prepared;
        Assert.True(prepared.Success,string.Join(',',prepared.Diagnostics.Select(d=>d.Code)));
        var elements=prepared.Request!.Circuit.Elements;
        var sources=elements.OfType<VoltageScaledCccs>().ToArray();Assert.Equal(2,sources.Length);
        Assert.All(sources,s=>Assert.Equal(-1.0/50,s.VoltageCoefficient));
        var charge=Assert.Single(elements.OfType<JunctionStorage>(),q=>q.EarlyEffect is not null);
        Assert.All(sources,s=>{Assert.Equal(charge.EarlyEffect!.Cp,s.Cp);Assert.Equal(charge.EarlyEffect.Cn,s.Cn);});
        var b=Assert.Single(elements.OfType<Resistor>(),r=>r.Ohms==10).N;
        var c=Assert.Single(elements.OfType<Resistor>(),r=>r.Ohms==20).N;
        var e=Assert.Single(elements.OfType<Resistor>(),r=>r.Ohms==5).N;
        Assert.Equal(sign==1?b:c,charge.EarlyEffect!.Cp);Assert.Equal(sign==1?c:b,charge.EarlyEffect.Cn);
        Assert.Equal(sign==1?b:e,charge.P);Assert.Equal(sign==1?e:b,charge.N);
        Assert.Empty(prepared.EarlyEffects); // Native law replaces the legacy outer DC gain iteration.
        Assert.All(elements.OfType<JunctionStorage>().Where(q=>q!=charge),q=>Assert.Null(q.EarlyEffect));
    }
    [Fact]
    public void Duplicate_early_aliases_refuse_instead_of_choosing_one()
    {
        var d=DeckParser.Parse("bad\nV1 b 0 .6\nQ1 b b 0 qm\n.model qm NPN(VA=50 VAF=100)\n.end\n",collectDeclarations:true);
        Assert.False(OperatingPointSelection.Select(d).Success);
    }
}
