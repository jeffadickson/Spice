using FsCheck;
using FsCheck.Fluent;
using Microsoft.FSharp.Core;
using Simulator.Syntax;
using Xunit;
namespace Simulator.Frontend.Tests;
public sealed class XspiceSummerTests
{
    private static XspiceSummerPreparation Prepare(string card="A1 [in1 in2] out sum",string model=".model sum summer",string parameters="")
    {
        var syntax=DeckParser.Parse($"summer\n{parameters}\n{card}\n{model}\n.end\n",collectDeclarations:true);
        Assert.Equal("accepted_structure",syntax.Status);var catalog=syntax.Declarations!;
        return XspiceSummerLowering.Prepare(catalog,Assert.Single(catalog.XspiceInstances),ParameterEvaluator.Evaluate(catalog));
    }
    [Fact]
    public void Omitted_arrays_expand_exact_unity_and_zero_defaults()
    {
        var result=Prepare("A1 [in1 in2 in3 in4] out sum");Assert.True(result.Success);
        Assert.Equal(4,result.Summer!.Inputs.Length);Assert.All(result.Summer.Inputs,i=>Assert.Equal(1,i.Gain));Assert.Equal(0,result.Summer.Offset);
    }
    [Fact]
    public void Weighted_offsets_and_differential_input_output_order_are_preserved()
    {
        var result=Prepare("A1 [%vd(a ar) %vd(b br)] %vd(out or) sum",".model sum summer(in_offset=[.25 -.5] in_gain=[2 -3] out_gain=4 out_offset=.1)");
        Assert.True(result.Success,string.Join(',',result.Diagnostics.Select(d=>d.Code)));
        Assert.Equal(new[]{8d,-12d},result.Summer!.Inputs.Select(i=>i.Gain));Assert.Equal(8.1,result.Summer.Offset);
        Assert.Equal("ar",result.Summer.Inputs[0].N.Text);Assert.Equal("br",result.Summer.Inputs[1].N.Text);Assert.Equal("or",result.Summer.OutputN.Text);
    }
    [Fact]
    public void Generated_arrays_reuse_model_lexical_parameters()
    {
        var result=Prepare(model:".model sum summer(in_gain=[{g} {-g}] in_offset=[{b} 0] out_gain={g})",parameters:".param g=2 b=.5");
        Assert.True(result.Success);Assert.Equal(new[]{4d,-4d},result.Summer!.Inputs.Select(i=>i.Gain));Assert.Equal(2,result.Summer.Offset);
    }
    [Theory]
    [InlineData("A1 %v[in1 in2] out sum")]
    [InlineData("A1 [%v(in1) in2] %v(out) sum")]
    public void Explicit_voltage_vector_and_scalar_ports_are_admitted(string card)=>Assert.True(Prepare(card).Success);
    [Theory]
    [InlineData("A1 [in1] out sum")]
    [InlineData("A1 %i[in1 in2] out sum")]
    [InlineData("A1 ~[in1 in2] out sum")]
    [InlineData("A1 [[in1] in2] out sum")]
    [InlineData("A1 [in1 null] out sum")]
    [InlineData("A1 [in1 %i(in2)] out sum")]
    [InlineData("A1 [in1 in2] [out] sum")]
    [InlineData("A1 [in1 in2] %i(out) sum")]
    public void Unsupported_vector_port_semantics_refuse_atomically(string card)
    {
        var result=Prepare(card);Assert.False(result.Success);Assert.Null(result.Summer);
    }
    [Theory]
    [InlineData(".model sum summer(in_gain=[1])")]
    [InlineData(".model sum summer(in_offset=[0 0 0])")]
    [InlineData(".model sum summer(in_gain=[])")]
    [InlineData(".model sum summer(in_gain=[1-2])")]
    [InlineData(".model sum summer(in_gain=[1+2])")]
    [InlineData(".model sum summer(in_gain=2)")]
    [InlineData(".model sum summer(unknown=1)")]
    [InlineData(".model sum summer(in_gain=[1 2] in_gain=[3 4])")]
    [InlineData(".model sum summer(in_gain=[{v(in1)} 1])")]
    [InlineData(".model sum summer(in_gain=[missing 1])")]
    [InlineData(".model sum summer(in_gain=[1e308 1] out_gain=2)")]
    [InlineData(".model sum summer(in_offset=[1e308 1e308] in_gain=[2 2])")]
    [InlineData(".model sum real_gain")]
    public void Wrong_lengths_types_parameters_and_nonfinite_results_are_not_ignored(string model)
    {
        var result=Prepare(model:model);Assert.False(result.Success);Assert.Null(result.Summer);Assert.NotEmpty(result.Diagnostics);
    }
    [Fact]
    public void Vector_ports_retain_bare_CR_physical_locations()
    {
        var syntax=DeckParser.Parse("t\rA1 [in1\r+ in2] out sum\r.model sum summer\r.end\r",collectDeclarations:true);
        Assert.Equal("accepted_structure",syntax.Status);var catalog=syntax.Declarations!;
        var result=XspiceSummerLowering.Prepare(catalog,Assert.Single(catalog.XspiceInstances),ParameterEvaluator.Evaluate(catalog));
        Assert.True(result.Success);Assert.Equal(3,result.Summer!.Inputs[1].P.Range.Line);Assert.Equal(3,result.Summer.Inputs[1].P.Range.Column);
    }
    [Fact]
    public void Weighted_sum_property_campaign()
    {
        var config=Config.QuickThrowOnFailure.WithMaxTest(128).WithReplay(FSharpOption<Replay>.Some(new(new Rnd(20261104UL),null)));
        Check.One(config,Prop.ForAll<short,short,short>((a,b,g)=>{
            var result=Prepare(model:$".model sum summer(in_gain=[{a} {b}] in_offset=[.25 -.5] out_gain={g} out_offset=1)");
            if(!result.Success)return false;var sum=result.Summer!;
            var actual=sum.Inputs[0].Gain*2+sum.Inputs[1].Gain*3+sum.Offset;
            var expected=(double)g*(a*2.25+b*2.5)+1;
            return actual==expected;
        }));
    }
}
