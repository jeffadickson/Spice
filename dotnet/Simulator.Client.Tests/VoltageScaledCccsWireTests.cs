using Simulator.Client;
using System.Text.Json;
using Xunit;

/// <summary>Generated DTO metadata retains the two independent controls and signed SI coefficient.</summary>
public sealed class VoltageScaledCccsWireTests
{
    [Fact]
    public void Typed_product_law_serializes_without_becoming_an_ordinary_scalar_CCCS()
    {
        var source=new VoltageScaledCccs("transport",3,0,2,0,"sensor",50,-0.02);
        var request=new SimulationRequest("product_wire",new(["0","sense","ctrl","out"],[source]),new OperatingPoint());
        var json=JsonSerializer.Serialize(request,WireFormat.Options);
        using var doc=JsonDocument.Parse(json);
        var element=doc.RootElement.GetProperty("circuit").GetProperty("elements")[0];
        Assert.Equal("voltage_scaled_cccs",element.GetProperty("kind").GetString());
        Assert.Equal("sensor",element.GetProperty("control_id").GetString());
        Assert.Equal(-0.02,element.GetProperty("voltage_coefficient").GetDouble());
        var roundtrip=JsonSerializer.Deserialize<SimulationRequest>(json,WireFormat.Options)!;
        Assert.Equal(source,Assert.IsType<VoltageScaledCccs>(Assert.Single(roundtrip.Circuit.Elements)));
    }
}
