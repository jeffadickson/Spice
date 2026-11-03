using System.Text.Json;
using Simulator.Syntax;
using Xunit;
namespace Simulator.Frontend.Tests;

/// <summary>Generated preparation contracts with independently established SPICE conventions.</summary>
public sealed class PreparationSemanticsTests
{
    private static SelectedOperatingPoint Prepare(string body)=>OperatingPointSelection.Select(DeckParser.Parse("Preparation\n"+body+"\n.end\n",collectDeclarations:true));
    [Fact]
    public void Current_controls_resolve_forward_voltage_branches_with_signed_SI_values()
    {
        var result=Prepare(".param gain=2 trans=3k\nF1 out 0 V1 {gain}\nH1 hv 0 V1 {trans}\nV1 drive 0 DC 1 AC 1\nR1 drive 0 1k\nR2 out 0 1k\nR3 hv 0 1k");
        Assert.True(result.Success,string.Join(',',result.Prepared.Diagnostics.Select(d=>d.Code)));
        using var json=JsonDocument.Parse(JsonSerializer.Serialize(result.Prepared.Request));
        var elements=json.RootElement.GetProperty("Circuit").GetProperty("Elements").EnumerateArray().ToArray();
        Assert.Equal(2,elements.Count(e=>e.GetProperty("kind").GetString() is "cccs" or "ccvs"));
    }
    [Fact]
    public void Current_controls_are_instance_local_in_repeated_subcircuits()
    {
        var result=Prepare("X1 a cell\nX2 b cell\n.subckt cell out\nF1 out 0 VS 2\nVS d 0 1\nR1 d 0 1k\nR2 out 0 1k\n.ends");
        Assert.True(result.Success,string.Join(',',result.Prepared.Diagnostics.Select(d=>d.Code)));
        Assert.Equal(2,result.Prepared.Origins.Count(o=>o.LocalName=="F1"));
        Assert.Equal(2,result.Prepared.Origins.Count(o=>o.LocalName=="VS"));
    }
    [Theory]
    [InlineData("F1 out 0 missing 2")]
    [InlineData("F1 out 0 R1 2")]
    [InlineData("H1 out 0 missing 1k")]
    [InlineData("H1 out 0 V1 POLY(1) 0 2")]
    public void Invalid_current_controls_refuse_atomically(string card)
    {
        var result=Prepare("V1 drive 0 1\nR1 drive 0 1k\nR2 out 0 1k\n"+card);
        Assert.False(result.Success);Assert.Null(result.Prepared.Request);Assert.Empty(result.Prepared.Origins);
    }
    [Fact]
    public void Global_node_precedes_a_conflicting_formal_port_as_native_ngspice_does()
    {
        var result=Prepare(".global vdd\nVDD vdd 0 3\nVP other 0 1\nX1 other out cell\n.subckt cell vdd out\nR1 vdd out 1k\nR2 out 0 1k\n.ends");
        Assert.True(result.Success,string.Join(',',result.Prepared.Diagnostics.Select(d=>d.Code)));
        var resistor=result.Prepared.Request!.Circuit.Elements.Single(e=>result.Prepared.Origins.Any(o=>o.WireId==e.Id&&o.LocalName=="R1"));
        var supply=result.Prepared.Request.Circuit.Elements.Single(e=>result.Prepared.Origins.Any(o=>o.WireId==e.Id&&o.LocalName=="VDD"));
        Assert.Equal(supply.P,resistor.P);
    }
    [Fact]
    public void Shadowed_actual_port_does_not_create_an_unconnected_unknown()
    {
        var result=Prepare(".global vdd\nVDD vdd 0 3\nX1 unused out cell\n.subckt cell vdd out\nR1 vdd out 1k\nR2 out 0 1k\n.ends");
        Assert.True(result.Success);Assert.False(result.Prepared.RootNodes.ContainsKey("unused"));
        Assert.Equal(3,result.Prepared.Request!.Circuit.Nodes.Length);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_global_in_an_unused_body_is_inert_until_that_body_is_instantiated(bool active)
    {
        var result=Prepare("VDD vdd 0 3\n"+(active?"X0 unused\n":"")+"X1 out cell\n.subckt unused\n.global vdd\n.ends\n.subckt cell out\nR1 vdd out 1k\nR2 out 0 1k\n.ends");
        Assert.True(result.Success,string.Join(',',result.Prepared.Diagnostics.Select(d=>d.Code)));
        var resistor=result.Prepared.Request!.Circuit.Elements.Single(e=>result.Prepared.Origins.Any(o=>o.WireId==e.Id&&o.LocalName=="R1"));
        var supply=result.Prepared.Request.Circuit.Elements.Single(e=>result.Prepared.Origins.Any(o=>o.WireId==e.Id&&o.LocalName=="VDD"));
        Assert.Equal(active,supply.P==resistor.P);
    }
    [Fact]
    public void Xspice_gain_is_lowered_without_exposing_synthetic_nodes_as_original_observables()
    {
        var result=Prepare("V1 in 0 DC 2 AC 1\nA1 in out amp\n.model amp gain(in_offset=.5 gain=3 out_offset=-1)\nR1 out 0 1k");
        Assert.True(result.Success,string.Join(',',result.Prepared.Diagnostics.Select(d=>d.Code)));
        Assert.Equal(new[]{"in","out"},result.Prepared.RootNodes.Keys.Order().ToArray());
        Assert.Equal(2,result.Prepared.AcSources.Length);
        Assert.Single(result.Prepared.AcSources.Where(s=>s.Real==0&&s.Imag==0));
    }
}
