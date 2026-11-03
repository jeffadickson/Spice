using System.Text.Json;
using Simulator.Syntax;
using Xunit;
namespace Simulator.Frontend.Tests;

/// <summary>Generated preparation contracts with independently established SPICE conventions.</summary>
public sealed class PreparationSemanticsTests
{
    [Theory]
    [InlineData("V1 out gnd 1\nR1 out gnd 1k")]
    [InlineData(".global GND\nV1 out GND 1\nR1 out GND 1k")]
    [InlineData("X1 out cell\n.subckt cell out\nV1 out GnD 1\nR1 out GnD 1k\n.ends")]
    [InlineData("V1 in 0 1\nB1 out 0 V=v(gnd)+v(in)\nR1 out 0 1k")]
    public void Default_ground_alias_normalizes_without_allocating_a_floating_node(string body)
    {
        var result=Prepare(body);
        Assert.True(result.Success,string.Join(',',result.Prepared.Diagnostics.Select(d=>d.Code)));
        Assert.NotNull(result.Prepared.Request);
        Assert.DoesNotContain(result.Prepared.RootNodes.Keys,n=>n.Equals("gnd",StringComparison.OrdinalIgnoreCase));
    }
    [Fact]
    public void Default_ground_formal_does_not_allocate_its_ignored_actual()
    {
        var result=Prepare("V1 in 0 3\nR1 out 0 1k\nX1 out not_used cell\n.subckt cell p gnd\nR2 p gnd 1k\n.ends");
        Assert.True(result.Success,string.Join(',',result.Prepared.Diagnostics.Select(d=>d.Code)));
        Assert.False(result.Prepared.RootNodes.ContainsKey("not_used"));
        Assert.Equal(3,result.Prepared.Request!.Circuit.Nodes.Length);
        Assert.Equal(0,result.Prepared.Request.Circuit.Elements.Last().N);
    }
    [Fact]
    public void Unused_ground_alias_declarations_and_bodies_do_not_change_a_valid_circuit()
    {
        var result=Prepare(".global gnd\nV1 out 0 1\nR1 out 0 1k\n.subckt unused\nV2 gnd 0 2\n.ends");
        Assert.True(result.Success);
        Assert.DoesNotContain(result.Prepared.RootNodes.Keys,n=>n.Equals("gnd",StringComparison.OrdinalIgnoreCase));
    }
    [Theory]
    [InlineData("rm","R(R=2k)",2000)]
    [InlineData("rm L=10u W=2u","R(RSH=100 SHORT=.5u NARROW=.25u)",600)]
    public void Named_resistor_profiles_execute_through_generated_preparation(string tail,string model,double expected)
    {
        var result=Prepare("V1 out 0 1\nR1 out 0 "+tail+"\n.model rm "+model);
        Assert.True(result.Success,string.Join(',',result.Prepared.Diagnostics.Select(d=>d.Code)));
        Assert.Equal(expected,Assert.Single(result.Prepared.Request!.Circuit.Elements.OfType<Simulator.Client.Resistor>()).Ohms,9);
        Assert.DoesNotContain(CircuitReadiness.Audit(DeckParser.Parse("Resistor\nV1 out 0 1\nR1 out 0 "+tail+"\n.model rm "+model+"\n.end\n",collectDeclarations:true)).Issues,i=>i.Code=="unsupported_primitive_header");
    }
    [Theory]
    [InlineData("summer")]
    [InlineData("summer(in_gain=[2 -3] in_offset=[.25 -.5] out_gain=4 out_offset=.1)")]
    public void Analog_summer_vectors_lower_to_existing_voltage_elements(string model)
    {
        var result=Prepare("V1 in 0 DC 1 AC 1\nV2 other 0 2\nA1 [in other] out sum\n.model sum "+model+"\nR1 out 0 1k");
        Assert.True(result.Success,string.Join(',',result.Prepared.Diagnostics.Select(d=>d.Code)));
        Assert.Equal(new[]{"in","other","out"},result.Prepared.RootNodes.Keys.Order().ToArray());
        Assert.Contains(result.Prepared.AcSources,s=>s.Real==0&&s.Imag==0);
    }
    [Theory]
    [InlineData("V=2*v(in)+.25")]
    [InlineData("I=1m*v(in)+2m")]
    [InlineData("V=1.25")]
    public void Affine_behavior_uses_existing_elements_and_explicit_zero_AC_offsets(string expression)
    {
        var result=Prepare("V1 in 0 DC 2 AC 1\nB1 out 0 "+expression+"\nR1 out 0 1k");
        Assert.True(result.Success,string.Join(',',result.Prepared.Diagnostics.Select(d=>d.Code)));
        Assert.Equal(new[]{"in","out"},result.Prepared.RootNodes.Keys.Order().ToArray());
        Assert.Contains(result.Prepared.AcSources,s=>s.Real==0&&s.Imag==0);
    }
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
