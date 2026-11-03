using Xunit;
namespace Simulator.Client.Tests;

/// <summary>Optional genuine engine checks of existing native current-control stamps through the generated client serializer.</summary>
public sealed class CurrentControlNativeTheoryAttribute:TheoryAttribute
{
    /// <summary>Requires the explicitly selected native executable; absence is a skip.</summary>
    public CurrentControlNativeTheoryAttribute(){if(!File.Exists(Environment.GetEnvironmentVariable("SPICE_TEST_EXECUTABLE")))Skip="Set SPICE_TEST_EXECUTABLE to the existing verified Rust engine.";}
}
/// <summary>Independent KCL/voltage-law expectations, including source-current polarity and SI transresistance.</summary>
public sealed class CurrentControlWireTests
{
    [CurrentControlNativeTheory]
    [InlineData("lu")]
    [InlineData("faer")]
    public async Task Both_current_control_contracts_round_trip_the_real_native_API(string backend)
    {
        var circuit=new Circuit(["0","drive","out","hv"],[
            new Voltage("control",1,0,1),new Resistor("sense_load",1,0,1000),
            new Cccs("f",2,0,"control",2),new Resistor("f_load",2,0,1000),
            new Ccvs("h",3,0,"control",3000),new Resistor("h_load",3,0,1000)]);
        using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var response=await new SimulationClient(Environment.GetEnvironmentVariable("SPICE_TEST_EXECUTABLE")!).RunAsync(new("current-control-wire",circuit,new OperatingPoint(),backend),deadline.Token);
        Assert.True(response.Succeeded,response.Payload.GetRawText());
        // I(control)=-1mA. F injects2mA into its1k load; H sets-3V.
        Assert.InRange(Math.Abs(response.FinalValue("i(control)")+.001),0,1e-12);
        Assert.InRange(Math.Abs(response.FinalValue("v(out)")-2),0,1e-9);
        Assert.InRange(Math.Abs(response.FinalValue("v(hv)")+3),0,1e-9);
    }
}
