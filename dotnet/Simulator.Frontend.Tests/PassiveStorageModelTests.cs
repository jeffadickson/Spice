using Simulator.Client;
using Simulator.Frontend;
using Simulator.Syntax;
using Xunit;
namespace Simulator.Frontend.Tests;
public sealed class PassiveStorageModelTests
{
    [Theory]
    [InlineData("CM 1p", ".model CM C ()", 1e-12)]
    [InlineData("CM C=1p", ".model CM C ()", 1e-12)]
    [InlineData("CM L=20u W=1u", ".model CM C (CJ=1)", 20e-12)]
    [InlineData("CM L=20u W=1u", ".model CM C (CAP=2p CJ=1)", 2e-12)]
    [InlineData("CM 1p", ".model CM C (CAP=2p)", 1e-12)]
    [InlineData("CM L=20u W=1u", ".model CM C (C=2 CJ=1)", 20e-12)]
    [InlineData("CM 1p TEMP=127 TC1=.01", ".model CM C (TC1=-.01)", 2e-12)]
    public void Scalar_geometry_and_instance_precedence_produce_physical_farads(string instance,string model,double expected)
    {
        var plan=RequestedAnalysis.Prepare(DeckParser.Parse($"cap\nV1 in 0 1\nR1 in out 1k\nC1 out 0 {instance}\n{model}\n.op\n.end\n",collectDeclarations:true));
        Assert.True(plan.Success,string.Join(',',plan.Diagnostics.Select(d=>d.Code)));
        var capacitor=Assert.Single(Assert.Single(Assert.Single(plan.Operations).Requests).Circuit.Elements.OfType<Capacitor>());
        Assert.True(Math.Abs(expected-capacitor.Farads)<1e-24);
    }
    [Theory]
    [InlineData("CM C=1p C=2p", ".model CM C ()")]
    [InlineData("CM 1p UNKNOWN=2", ".model CM C ()")]
    [InlineData("CM L=-1u W=1u", ".model CM C (CJ=1)")]
    [InlineData("CM 1p TEMP=-274", ".model CM C ()")]
    [InlineData("CM 1p IC=1", ".model CM C ()")]
    [InlineData("CM 1p", ".model CM C (UNKNOWN=2)")]
    public void Unsupported_or_invalid_storage_semantics_publish_no_partial_plan(string instance,string model)
    {
        var plan=RequestedAnalysis.Prepare(DeckParser.Parse($"bad cap\nV1 in 0 1\nC1 in 0 {instance}\n{model}\n.op\n.end\n",collectDeclarations:true));
        Assert.False(plan.Success);Assert.Empty(plan.Operations);
    }
    [FsCheck.Xunit.Property(MaxTest=64,Replay="1601,20261117")]
    public bool Explicit_geometry_obeys_area_homogeneity(int input)
    {
        var width=1+(int)((uint)input%64);
        double Read(int scale)
        {
            var text=$"area\nV1 in 0 1\nC1 in 0 CM L=2u W={width*scale}u\n.model CM C (CJ=3)\n.op\n.end\n";
            var plan=RequestedAnalysis.Prepare(DeckParser.Parse(text,collectDeclarations:true));
            return plan.Success?plan.Operations[0].Requests[0].Circuit.Elements.OfType<Capacitor>().Single().Farads:double.NaN;
        }
        var first=Read(1);var doubled=Read(2);
        return double.IsFinite(first)&&first>0&&Math.Abs(doubled/first-2)<1e-14;
    }
}
