using FsCheck;
using FsCheck.Xunit;
using Simulator.Syntax;
using Xunit;
namespace Simulator.Frontend.Tests;

public sealed class XspiceGainTests
{
    private static XspiceGainPreparation Prepare(string card="A1 in out amp",string model=".model amp gain",string parameters="")
    {
        var syntax=DeckParser.Parse($"gain test\n{parameters}\n{card}\n{model}\n.end\n",collectDeclarations:true);
        Assert.Equal("accepted_structure",syntax.Status);
        var catalog=syntax.Declarations!;
        return XspiceGainLowering.Prepare(catalog,Assert.Single(catalog.XspiceInstances),ParameterEvaluator.Evaluate(catalog));
    }

    [Fact]
    public void Default_gain_preserves_single_ended_voltage_ports()
    {
        var result=Prepare();Assert.True(result.Success);var gain=result.Gain!;
        Assert.Equal(1,gain.Gain);Assert.Equal(0,gain.Offset);
        Assert.Equal("in",gain.InputP.Text);Assert.Equal("0",gain.InputN.Text);
        Assert.Equal("out",gain.OutputP.Text);Assert.Equal("0",gain.OutputN.Text);
        Assert.Equal(3,gain.Range.Line);
    }

    [Theory]
    [InlineData("A1 %v(in) %v(out) amp")]
    [InlineData("A1 %v in %v out amp")]
    public void Explicit_scalar_voltage_types_are_supported(string card)=>Assert.True(Prepare(card).Success);

    [Fact]
    public void Differential_polarity_and_evaluated_offset_are_exact()
    {
        var result=Prepare("A1 %vd(ip im) %vd(op om) amp",".model amp gain(in_offset={bias} gain={scale} out_offset=-1)",".param bias=0.25 scale=4");
        Assert.True(result.Success);var gain=result.Gain!;
        Assert.Equal(4,gain.Gain);Assert.Equal(0,gain.Offset);
        Assert.Equal("ip",gain.InputP.Text);Assert.Equal("im",gain.InputN.Text);
        Assert.Equal("op",gain.OutputP.Text);Assert.Equal("om",gain.OutputN.Text);
    }

    [Theory]
    [InlineData("A1 null out amp")]
    [InlineData("A1 ~in out amp")]
    [InlineData("A1 [in] out amp")]
    [InlineData("A1 (in) out amp")]
    [InlineData("A1 %i(in) out amp")]
    [InlineData("A1 %id(in 0) out amp")]
    [InlineData("A1 %vnam(V1) out amp")]
    [InlineData("A1 in %i(out) amp")]
    [InlineData("A1 %d(in) out amp")]
    [InlineData("A1 in middle out amp")]
    [InlineData("A1 in amp")]
    public void Unsupported_port_semantics_are_atomic_refusals(string card)
    {
        var result=Prepare(card);Assert.False(result.Success);Assert.Null(result.Gain);
        Assert.Equal("unsupported_xspice_gain_ports",Assert.Single(result.Diagnostics).Code);
    }

    [Theory]
    [InlineData(".model amp summer", "unsupported_xspice_model")]
    [InlineData(".model amp gain(foo=1)", "unsupported_xspice_parameter")]
    [InlineData(".model amp gain(gain=2 gain=3)", "unsupported_xspice_parameter")]
    [InlineData(".model amp gain(gain=[1 2])", "unsupported_expression")]
    [InlineData(".model amp gain(gain={missing})", "missing_parameter")]
    [InlineData(".model amp gain(gain={v(in)})", "unsupported_expression")]
    [InlineData(".model amp gain(in_offset=1e308 gain=2)", "nonfinite_xspice_offset")]
    public void Unsupported_parameters_are_not_silently_ignored(string model,string code)
    {
        var result=Prepare(model:model);Assert.False(result.Success);Assert.Null(result.Gain);
        Assert.Equal(code,Assert.Single(result.Diagnostics).Code);
    }

    [Fact]
    public void Missing_ambiguous_and_deferred_models_are_refused()
    {
        foreach(var model in new[]{"", ".model amp gain\n.model amp gain", ".include external.lib\n.model amp gain"})
            Assert.Equal("unbound_xspice_model",Assert.Single(Prepare(model:model).Diagnostics).Code);
    }

    [Fact]
    public void Foreign_environment_and_instance_are_refused()
    {
        var syntax=DeckParser.Parse("t\nA1 in out amp\n.model amp gain(gain=2)\n.end\n",collectDeclarations:true);
        var catalog=syntax.Declarations!;var instance=Assert.Single(catalog.XspiceInstances);
        var foreign=DeckParser.Parse("t\n.end\n",collectDeclarations:true).Declarations!;
        Assert.Equal("foreign_xspice_instance",Assert.Single(XspiceGainLowering.Prepare(foreign,instance,ParameterEvaluator.Evaluate(foreign)).Diagnostics).Code);
        Assert.Equal("invalid_parameter_environment",Assert.Single(XspiceGainLowering.Prepare(catalog,instance,ParameterEvaluator.Evaluate(foreign)).Diagnostics).Code);
    }

    [Property(MaxTest=128,Replay="571,20261102")]
    public bool Affine_parameters_preserve_gain_sign_and_offset(short gain,short inputOffset,short outputOffset)
    {
        var result=Prepare(model:$".model amp gain(gain={gain} in_offset={inputOffset} out_offset={outputOffset})");
        return result.Success && result.Gain!.Gain==gain && result.Gain.Offset==(double)gain*inputOffset+outputOffset;
    }
}
