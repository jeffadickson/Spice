using FsCheck;
using FsCheck.Xunit;
using Simulator.Client;
using Simulator.Syntax;
using Xunit;
namespace Simulator.Frontend.Tests;

public sealed class SelectedOperatingPointTests
{
    private static DeckSyntax Parse(string body)=>DeckParser.Parse("Selected DC\n"+body+"\n.end\n",collectDeclarations:true);
    [Fact]
    public void Explicit_static_selection_records_analyses_controls_and_observation_omissions()
    {
        var syntax=Parse("V1 in 0 3\nR1 in out 1k\nC1 out 0 1u\n.tran 1u 1m\n.ac dec 10 1 1k\n.print tran v(out)\n.save v(out)\n.control\necho MUST_NOT_RUN\ntran 1u 1m\n.endc");
        Assert.False(OperatingPointBridge.Lower(syntax).Success);
        var selected=OperatingPointSelection.Select(syntax);
        Assert.True(selected.Success,string.Join(',',selected.Prepared.Diagnostics.Select(d=>d.Code)));
        Assert.IsType<OperatingPoint>(selected.Prepared.Request!.Analysis);
        Assert.Single(selected.Prepared.Request.Circuit.Elements.OfType<Capacitor>());
        Assert.Equal(5,selected.Omissions.Length);
        Assert.Contains(selected.Omissions,o=>o.Kind=="control"&&o.Reason=="input_control_not_executed");
        Assert.Equal(2,selected.Omissions.Count(o=>o.Reason=="analysis_not_selected"));
        Assert.Equal(2,selected.Omissions.Count(o=>o.Reason=="observation_not_selected"));
    }
    [Theory]
    [InlineData(".global power")]
    [InlineData(".ic v(a)=2")]
    [InlineData(".options reltol=1e-2")]
    [InlineData("B1 a 0 V=1")]
    [InlineData("V2 b 0 SFFM(0 1 1k 2 1)")]
    [InlineData(".end\nR2 b 0 1k")]
    public void Unknown_physical_semantics_never_produce_partial_selected_requests(string unsupported)
    {
        var selected=OperatingPointSelection.Select(Parse("V1 a 0 3\nR1 a 0 1k\n.tran 1u 1m\n"+unsupported));
        Assert.False(selected.Success);Assert.Null(selected.Prepared.Request);Assert.Empty(selected.Prepared.Origins);Assert.Empty(selected.Omissions);
    }
    [Theory]
    [InlineData("C1 a 0 0","invalid_capacitance")]
    [InlineData("L1 a 0 -1","invalid_inductance")]
    [InlineData("L1 a 0 0","invalid_inductance")]
    [InlineData("R1 a 0 0","invalid_resistance")]
    public void Selected_physical_values_are_checked_before_execution(string card,string error)
    {
        var selected=OperatingPointSelection.Select(Parse("V1 a 0 1\n"+card));
        Assert.False(selected.Success);Assert.Contains(selected.Prepared.Diagnostics,d=>d.Code==error);
    }
    [Fact]
    public void Active_hierarchical_inductors_and_capacitors_keep_SI_and_node_identity()
    {
        var syntax=Parse("V1 a 0 3\nX1 a 0 cell\n.subckt cell p n\nR1 p middle 1k\nL1 middle n 2m\nC1 p middle 3u\n.ends\n.op");
        var selected=OperatingPointSelection.Select(syntax,backend:"faer");Assert.True(selected.Success);
        var circuit=selected.Prepared.Request!.Circuit;
        Assert.Equal(.002,Assert.Single(circuit.Elements.OfType<Inductor>()).Henries);
        Assert.Equal(.000003,Assert.Single(circuit.Elements.OfType<Capacitor>()).Farads);
        Assert.Equal(4,selected.Prepared.Origins.Length);Assert.Equal("faer",selected.Prepared.Request.Backend);
        Assert.Single(selected.Omissions);Assert.Equal("op",selected.Omissions[0].Kind);
    }
    [Fact]
    public void Controls_inside_unused_bodies_are_also_marked_for_oracle_exclusion()
    {
        var selected=OperatingPointSelection.Select(Parse("V1 a 0 1\nR1 a 0 1k\n.subckt unused p n\n.control\necho MUST_NOT_RUN\n.endc\n.ends"));
        Assert.True(selected.Success);Assert.Contains(selected.Omissions,o=>o.Kind=="control"&&o.ScopeId!=0);
    }
    [Fact]
    public void Unused_bodies_cannot_hide_potentially_global_physical_directives()
    {
        var selected=OperatingPointSelection.Select(Parse("V1 a 0 1\nR1 a 0 1k\n.subckt unused p n\n.global a\n.ends"));
        Assert.False(selected.Success);Assert.Null(selected.Prepared.Request);Assert.Empty(selected.Omissions);
    }
    [Fact]
    public void Rejected_syntax_has_no_selected_request_or_omission_plan()
    {
        var selected=OperatingPointSelection.Select(Parse("R1 a 0"));
        Assert.False(selected.Success);Assert.Null(selected.Prepared.Request);Assert.Empty(selected.Omissions);
        Assert.Contains(selected.Prepared.Diagnostics,d=>d.Code=="syntax_not_accepted");
    }
    [Property(MaxTest=128,Replay="908,20261101")]
    public bool Arbitrary_bounded_selected_input_never_publishes_partial_state(NonNull<string> input)
    {
        var selected=OperatingPointSelection.Select(Parse(input.Get[..Math.Min(128,input.Get.Length)]));
        var result=selected.Prepared;
        if(!selected.Success)return result.Request==null&&result.Origins.IsEmpty&&result.RootNodes.IsEmpty&&selected.Omissions.IsEmpty;
        var circuit=result.Request!.Circuit;
        return result.Request.Analysis is OperatingPoint&&circuit.Elements.Length>0&&circuit.Nodes.Length<=128
            &&circuit.Elements.All(e=>e.P>=0&&e.P<circuit.Nodes.Length&&e.N>=0&&e.N<circuit.Nodes.Length&&e.P!=e.N);
    }
    [Property(MaxTest=128,Replay="907,20261101")]
    public bool Positive_passive_values_and_observation_directives_preserve_typed_selection(PositiveInt sample)
    {
        var r=1+sample.Get%100_000;
        var selected=OperatingPointSelection.Select(Parse($"V1 in 0 3\nR1 in out {r}\nC1 out 0 1u\n.plot tran v(out)\n.tran 1u 1m"));
        return selected.Success&&selected.Prepared.Request!.Circuit.Elements.OfType<Resistor>().Single().Ohms==r
            &&selected.Omissions.Length==2&&selected.Prepared.Request.Analysis is OperatingPoint;
    }
}
