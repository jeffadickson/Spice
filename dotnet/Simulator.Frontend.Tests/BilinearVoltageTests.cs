using System.Text.Json;
using Simulator.Client;
using Simulator.Frontend;
using Simulator.Syntax;
using Xunit;
namespace Simulator.Frontend.Tests;
/// <summary>Bilinear laws must use an explicit zero-bias product, never cancellation of a unit factor.</summary>
public sealed class BilinearVoltageTests
{
    private static RequestedAnalysisPlan Plan(string law,string analysis=".dc VA -2 2 1")=>RequestedAnalysis.Prepare(DeckParser.Parse($"bilinear\nVA a 0 2\nVB b 0 3\nVC c 0 100\nB1 out 0 V={{{law}}}\nR1 out 0 1000\n{analysis}\n.end\n",collectDeclarations:true));
    [Theory]
    [InlineData("v(c)+v(a)*v(b)")]
    [InlineData("3+2*v(c)-4*v(a)*v(b)")]
    [InlineData("v(a,c)*v(b,c)")]
    public void Products_retain_the_existing_native_multiplier_with_zero_factor_offset(string law)
    {
        var plan=Plan(law);Assert.True(plan.Success,string.Join(',',plan.Diagnostics.Select(d=>d.Code)));
        Assert.IsType<OperatingPointNewton>(plan.Operations[0].Requests[0].Analysis);
        var product=Assert.Single(plan.Operations[0].Requests[0].Circuit.Elements.OfType<VoltageScaledCccs>());
        using var wire=JsonDocument.Parse(JsonSerializer.Serialize<Element>(product,WireFormat.Options));
        Assert.Equal(0,wire.RootElement.GetProperty("factor_offset").GetDouble());
        Assert.Contains(plan.Operations[0].Requests[0].Circuit.Elements,e=>e.Id==product.ControlId&&e is Voltage or Vcvs);
    }
    [Theory]
    [InlineData("v(a)*v(b)*v(c)")]
    [InlineData("v(a)/v(b)")]
    [InlineData("sin(v(a))*v(b)")]
    [InlineData("v(a)*time")]
    public void Unsupported_factors_are_atomic_refusals(string law)
    {var plan=Plan(law);Assert.False(plan.Success);Assert.Empty(plan.Operations);}
    [Theory]
    [InlineData(".ac lin 1 1 1")]
    [InlineData(".tran 1u 2u")]
    public void Unqualified_analyses_remain_refused(string analysis)
    {var plan=Plan("v(a)*v(b)",analysis);Assert.False(plan.Success);Assert.Empty(plan.Operations);}
    [Fact]
    public void Product_count_is_bounded_before_a_request_is_published()
    {var plan=Plan(string.Join('+',Enumerable.Repeat("v(a)*v(b)",9)));Assert.False(plan.Success);Assert.Empty(plan.Operations);}
}
