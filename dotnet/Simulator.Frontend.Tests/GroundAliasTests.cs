using Simulator.Syntax;
using Xunit;
namespace Simulator.Frontend.Tests;

public sealed class GroundAliasTests
{
    private const string Formal="V1 in 0 3\nR1 out 0 1k\nX1 out in cell\n.subckt cell p gnd\nR2 p gnd 1k\n.ends";
    internal static DeckSyntax Parse(string body)=>DeckParser.Parse("Ground policy\n"+body+"\n.op\n.end\n",collectDeclarations:true);

    [Theory]
    [InlineData(GroundAliasPolicy.Automatic,true)]
    [InlineData(GroundAliasPolicy.Ngspice,true)]
    [InlineData(GroundAliasPolicy.Pspice,false)]
    [InlineData(GroundAliasPolicy.Disabled,false)]
    public void Formal_binding_retains_the_native_mode_distinction(GroundAliasPolicy policy,bool grounded)
    {
        var result=OperatingPointSelection.Select(Parse(Formal),new(){GroundAliases=policy});
        Assert.True(result.Success,string.Join(',',result.Prepared.Diagnostics.Select(d=>d.Code)));
        Assert.Equal(grounded,result.Prepared.Request!.Circuit.Elements.Last().N==0);
        Assert.False(result.Prepared.RootNodes.ContainsKey("gnd"));
        Assert.Equal(policy==GroundAliasPolicy.Automatic?GroundAliasPolicy.Ngspice:policy,result.Prepared.GroundAliases);
    }
    [Fact]
    public void Disabled_auto_ground_preserves_an_independently_driven_root_node()
    {
        var syntax=Parse("Vref gnd 0 2\nV1 out gnd 1\nR1 out 0 1k");
        Assert.False(OperatingPointSelection.Select(syntax).Success);
        var literal=OperatingPointSelection.Select(syntax,new(){GroundAliases=GroundAliasPolicy.Disabled});
        Assert.True(literal.Success);
        Assert.True(literal.Prepared.RootNodes.ContainsKey("gnd"));
    }
    internal const string Switch="\nVaux aux 0 0\nVc ctrl 0 .5\nS1 aux 0 ctrl 0 sw\n.model sw vswitch(ron=10 roff=1e6 von=1 voff=0)";
    [Fact]
    public void Automatic_switch_compatibility_resolves_ground_policy_before_elaboration()
    {
        var syntax=Parse(Formal+Switch);
        var automatic=OperatingPointSelection.Select(syntax);
        Assert.True(automatic.Success);Assert.Equal(GroundAliasPolicy.Pspice,automatic.Prepared.GroundAliases);
        Assert.NotEqual(0,automatic.Prepared.Request!.Circuit.Elements.OfType<Simulator.Client.Resistor>().Last().N);
        var incompatible=OperatingPointSelection.Select(syntax,new(){GroundAliases=GroundAliasPolicy.Ngspice});
        Assert.False(incompatible.Success);Assert.Contains(incompatible.Prepared.Diagnostics,d=>d.Code=="unsupported_ground_dialect_combination");
    }
    [Theory]
    [InlineData("gnd.cell","")]
    [InlineData("cell"," params: unused=gnd")]
    [InlineData("GND.cell","")]
    [InlineData("cell"," params: unused=GND")]
    public void Pspice_header_identifier_boundaries_include_names_and_parameter_values(string name,string parameters)
    {
        var syntax=Parse($".param gnd=2\nV1 in 0 3\nR1 out 0 1k\nX1 out in {name}\n.subckt {name} p ret{parameters}\nVlocal gnd 0 1\nR2 p gnd 1k\n.ends");
        var selected=OperatingPointSelection.Select(syntax,new(){GroundAliases=GroundAliasPolicy.Pspice});
        Assert.True(selected.Success,string.Join(',',selected.Prepared.Diagnostics.Select(d=>d.Code)));
    }
    [Fact]
    public void Pspice_ordinary_headers_still_normalize_local_ground()
    {
        var syntax=Parse("V1 in 0 3\nR1 out 0 1k\nX1 out in cell\n.subckt cell p ret\nR2 p gnd 1k\nR3 p ret 1k\n.ends");
        var result=OperatingPointSelection.Select(syntax,new(){GroundAliases=GroundAliasPolicy.Pspice});
        Assert.True(result.Success);
        var wire=result.Prepared.Origins.Single(o=>o.LocalName=="R2").WireId;
        Assert.Equal(0,result.Prepared.Request!.Circuit.Elements.Single(e=>e.Id==wire).N);
    }
    [Fact]
    public void Default_mode_globals_and_unused_Pspice_globals_do_not_trigger_protected_refusal()
    {
        var active=OperatingPointSelection.Select(Parse("V1 in 0 3\nR1 out 0 1k\nX1 out in cell\n.subckt cell p gnd\n.global gnd vdd\nR2 p gnd 1k\n.ends"));
        Assert.True(active.Success);
        var unused=OperatingPointSelection.Select(Parse("V1 out 0 1\nR1 out 0 1k\n.subckt cell p gnd\n.global gnd vdd\nR2 p gnd 1k\n.ends"),new(){GroundAliases=GroundAliasPolicy.Pspice});
        Assert.True(unused.Success);Assert.Equal(2,unused.Prepared.Request!.Circuit.Nodes.Length);
    }
    [Fact]
    public void Literal_zero_formal_ignores_actual_in_every_selected_mode()
    {
        var syntax=Parse("R1 out 0 1k\nX1 out unused cell\n.subckt cell p 0\nR2 p 0 1k\n.ends");
        foreach(var policy in new[]{GroundAliasPolicy.Ngspice,GroundAliasPolicy.Pspice,GroundAliasPolicy.Disabled}){
            var result=OperatingPointSelection.Select(syntax,new(){GroundAliases=policy});
            Assert.True(result.Success);Assert.False(result.Prepared.RootNodes.ContainsKey("unused"));
        }
        Assert.Contains(OperatingPointBridge.Lower(syntax).Diagnostics,d=>d.Code=="reserved_formal_ground");
    }
    [Fact]
    public void Ambiguous_protected_global_and_nested_Pspice_contexts_refuse_atomically()
    {
        foreach(var tail in new[]{".global gnd",".global gnd vdd",".subckt inner p\nR3 p 0 1k\n.ends inner"}){
            var syntax=Parse("V1 in 0 3\nR1 out 0 1k\nX1 out in cell\n.subckt cell p gnd\n"+tail+"\nR2 p gnd 1k\n.ends");
            var result=OperatingPointSelection.Select(syntax,new(){GroundAliases=GroundAliasPolicy.Pspice});
            Assert.False(result.Success);Assert.Null(result.Prepared.Request);Assert.Empty(result.Prepared.RootNodes);
            Assert.Contains(result.Prepared.Diagnostics,d=>d.Code.StartsWith("unsupported_ground_",StringComparison.Ordinal));
        }
    }
    [Fact]
    public void Unsupported_enum_is_rejected()=>Assert.Throws<ArgumentOutOfRangeException>(()=>
        OperatingPointSelection.Select(Parse("R1 out 0 1k"),new(){GroundAliases=(GroundAliasPolicy)999}));

    [Theory]
    [InlineData("E1 out 0 gnd 0 2")]
    [InlineData("G1 out 0 gnd 0 2")]
    [InlineData("B1 out 0 V=v(gnd)")]
    [InlineData("B1 out 0 I=v(gnd)")]
    [InlineData("A1 gnd out amp\n.model amp gain(gain=5 out_offset=1)")]
    [InlineData("A1 [ gnd 0 ] out amp\n.model amp summer(in_gain=[2 3] out_offset=1)")]
    [InlineData("A1 [ %v(gnd) %vd(0,gnd) ] out amp\n.model amp summer(in_gain=[2 3] out_offset=1)")]
    public void Zero_control_reductions_never_emit_invalid_control_terminals(string body)
    {
        var result=OperatingPointSelection.Select(Parse(body+"\nR1 out 0 1k"));
        Assert.True(result.Success,string.Join(',',result.Prepared.Diagnostics.Select(d=>d.Code)));
        Assert.DoesNotContain(result.Prepared.Request!.Circuit.Elements,e=>
            e is Simulator.Client.Vcvs v&&v.Cp==v.Cn||e is Simulator.Client.Vccs g&&g.Cp==g.Cn);
        foreach(var independent in result.Prepared.Request.Circuit.Elements.Where(e=>e is Simulator.Client.Voltage or Simulator.Client.Current))
            Assert.Contains(result.Prepared.AcSources,s=>s.Id==independent.Id&&s.Real==0&&s.Imag==0);
    }
    [Theory]
    [InlineData("[gnd 0]")][InlineData("[0 gnd]")][InlineData("[gnd gnd]")]
    [InlineData("[ [gnd 0] ]")]
    public void Native_unreplaced_bracket_aliases_refuse_instead_of_changing_topology(string vector)
    {
        var result=OperatingPointSelection.Select(Parse($"A1 {vector} out amp\n.model amp summer(in_gain=[2 3])\nR1 out 0 1k"));
        Assert.False(result.Success);
        Assert.Contains(result.Prepared.Diagnostics,d=>d.Code=="unsupported_ground_lexical_context");
        Assert.Null(result.Prepared.Request);Assert.Empty(result.Prepared.RootNodes);
    }

    [FsCheck.Xunit.Property(MaxTest=200,Replay="363,20261103")]
    public bool Alias_case_changes_do_not_change_root_topology(bool first,bool second,bool third)
    {
        var alias=new string([first?'G':'g',second?'N':'n',third?'D':'d']);
        var result=OperatingPointSelection.Select(Parse($"V1 out {alias} 1\nR1 out {alias} 1k"));
        return result.Success&&result.Prepared.Request!.Circuit.Nodes.Length==2&&
            result.Prepared.Request.Circuit.Elements.All(e=>e.N==0)&&!result.Prepared.RootNodes.ContainsKey(alias);
    }
}
