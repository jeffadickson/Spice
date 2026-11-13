using Simulator.Client;
using Simulator.Syntax;
using Xunit;
namespace Simulator.Frontend.Tests;

/// <summary>Resistance voltage dependence survives preparation; it is not frozen at a guessed control bias.</summary>
public sealed class BehavioralResistanceTests
{
    [Fact]
    public void Original_style_resistance_retains_runtime_input_and_temperature_scale_for_OP_and_AC()
    {
        var plan=Prepare("r={1k+v(ctrl)} tc1=.001", ".temp 127", "op\nac lin 1 1000 1000");
        Assert.True(plan.Success,string.Join(",",plan.Diagnostics.Select(d=>d.Code)));
        Assert.Equal(2,plan.Operations.Length);
        foreach(var operation in plan.Operations)
        {
            var resistor=Assert.Single(Assert.Single(operation.Requests).Circuit.Elements.OfType<BehavioralResistor>());
            var scale=Assert.IsType<ExpressionMultiply>(resistor.Expression);
            Assert.Equal(1.1,Assert.IsType<ExpressionConstant>(scale.Right).Value);
            Assert.IsType<ExpressionAdd>(scale.Left);
        }
    }

    [Theory]
    [InlineData("r={1k+v(ctrl)} unknown=1")]
    [InlineData("r={1k+v(ctrl)} tc1=.001 tc1=.002")]
    [InlineData("r={1k+v(ctrl)} tc1=-.01")]
    [InlineData("r={1k+v(ctrl)+v(other)}")]
    [InlineData("r={1k+i(V1)}")]
    [InlineData("r={1k+v(ctrl)} ac=1k")]
    public void Unsupported_physics_or_ambiguous_attributes_refuse_atomically(string header)
    {
        var plan=Prepare(header,".temp 127","op");
        Assert.False(plan.Success);Assert.Empty(plan.Operations);
    }

    [Fact]
    public void Strict_public_bridge_does_not_implicitly_opt_into_the_runtime_profile()
    {
        var syntax=DeckParser.Parse("Strict\nR1 a 0 r={1k+v(ctrl)}\nV1 a 0 10\nVc ctrl 0 0\n.op\n.end\n",collectDeclarations:true);
        Assert.False(OperatingPointBridge.Lower(syntax).Success);
    }

    [Fact]
    public void Transient_admission_waits_for_separate_history_qualification()
    {
        var plan=Prepare("r={1k+v(ctrl)}","","tran 1u 10u");
        Assert.False(plan.Success);Assert.Empty(plan.Operations);
        Assert.Contains(plan.Diagnostics,d=>d.Code=="unsupported_behavioral_resistance_analysis");
    }

    [Fact]
    public void Quadratic_temperature_term_scales_resistance_and_zero_factor_has_a_stable_diagnostic()
    {
        // 100 K above nominal: 1 + .001*100 + .00001*100^2 = 1.2.
        var plan=Prepare("r={1k+v(ctrl)} tc1=.001 tc2=.00001",".temp 127","op");
        Assert.True(plan.Success,string.Join(",",plan.Diagnostics.Select(d=>d.Code)));
        var resistor=Assert.Single(Assert.Single(Assert.Single(plan.Operations).Requests).Circuit.Elements.OfType<BehavioralResistor>());
        Assert.Equal(1.2,Assert.IsType<ExpressionConstant>(Assert.IsType<ExpressionMultiply>(resistor.Expression).Right).Value,14);
        var invalid=Prepare("r={1k+v(ctrl)} tc1=-.01",".temp 127","op");
        Assert.False(invalid.Success);Assert.Empty(invalid.Operations);
        Assert.Contains(invalid.Diagnostics,d=>d.Code=="invalid_behavioral_resistance_temperature_factor");
    }

    private static RequestedAnalysisPlan Prepare(string header,string temperature,string commands)
        =>RequestedAnalysis.Prepare(DeckParser.Parse("Resistance\nV1 a 0 dc=100 ac=10\nVc ctrl 0 dc=20 ac=1\nR1 a 0 "+header+"\n"+temperature+"\n.control\n"+commands+"\n.endc\n.end\n",collectDeclarations:true));
}
